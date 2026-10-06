namespace IdentityApi.Workers;
using System.Text;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Infrastructure.Messaging;
using IdentityApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

/// <summary>
/// Publishes unpublished <c>outbox_events</c> rows to the <c>crm.identity.events</c> topic exchange
/// (CLAUDE.md rule 3). Each row is wrapped in the platform envelope at publish time
/// (<see cref="OutboxEnvelope"/>); routing key = event type, MessageId = event_id = row id.
/// At-least-once with publisher confirms — consumers deduplicate via processed_events. With the
/// broker down it keeps retrying; request-path writes are unaffected. Logs ids/counts only.
/// </summary>
public class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connections,
    IOptions<RabbitMqSettings> rabbit,
    IOptions<WorkerSettings> workers,
    TimeProvider time,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private static readonly TimeSpan LagWarningInterval = TimeSpan.FromMinutes(1);

    private readonly RabbitMqSettings _settings = rabbit.Value;
    private readonly WorkerSettings _workers = workers.Value;

    private IConnection? _connection;
    private IModel? _channel;
    private DateTimeOffset _lastLagWarning = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.OutboxRelayEnabled)
        {
            logger.LogInformation("Outbox relay is disabled by configuration.");
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, _settings.RelayPollSeconds));
        var batchSize = Math.Max(1, _settings.RelayBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;

            try
            {
                published = await PublishBatchAsync(batchSize, stoppingToken);
                await WarnIfLaggingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Reset();
                logger.LogWarning(ex, "Outbox relay pass failed; unpublished rows stay in the outbox.");
            }

            if (published < batchSize)
            {
                try { await Task.Delay(poll, time, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        Reset();
    }

    /// <summary>One relay pass (publish one batch). Used by the loop and directly by tests.</summary>
    public Task<int> RunOnceAsync(CancellationToken ct = default)
        => PublishBatchAsync(Math.Max(1, _settings.RelayBatchSize), ct);

    private async Task<int> PublishBatchAsync(int batchSize, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var batch = await context.OutboxEvents
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.OccurredAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        var channel = EnsureChannel();
        if (channel is null) return 0;

        var published = 0;

        try
        {
            foreach (var row in batch)
            {
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.MessageId = row.Id.ToString();
                properties.Type = row.EventType;
                properties.Timestamp = new AmqpTimestamp(row.OccurredAt.ToUnixTimeSeconds());

                channel.BasicPublish(
                    exchange: _settings.Exchange,
                    routingKey: row.EventType,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(OutboxEnvelope.Build(row)));
            }

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));

            var now = time.GetUtcNow();
            foreach (var row in batch) row.PublishedAt = now;
            published = batch.Count;
        }
        catch (Exception ex)
        {
            foreach (var row in batch) row.PublishAttempts += 1;
            Reset();
            logger.LogWarning(ex,
                "Could not publish {Count} outbox row(s) (first {FirstEventId}); they stay unpublished and will be retried.",
                batch.Count, batch[0].Id);
        }

        await context.SaveChangesAsync(ct);
        return published;
    }

    private async Task WarnIfLaggingAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (now - _lastLagWarning < LagWarningInterval) return;

        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var backlog = await outbox.GetBacklogAsync(ct);
        if (backlog.OldestOccurredAt is not { } oldest) return;

        var ageSeconds = (long)(now - oldest).TotalSeconds;
        if (ageSeconds <= Math.Max(1, _settings.OutboxLagWarningSeconds)) return;

        _lastLagWarning = now;
        logger.LogWarning(
            "Outbox lag: oldest unpublished row is {AgeSeconds}s old, {UnpublishedCount} row(s) waiting for exchange {Exchange}.",
            ageSeconds, backlog.UnpublishedCount, _settings.Exchange);
    }

    private IModel? EnsureChannel()
    {
        if (_channel is { IsOpen: true }) return _channel;

        Reset();

        _connection = connections.TryConnect(out var error);
        if (_connection is null)
        {
            logger.LogWarning("The broker is unreachable ({Error}); events are waiting in the outbox.", error);
            return null;
        }

        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_settings.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ConfirmSelect();

        logger.LogInformation("Outbox relay connected; publishing to exchange {Exchange}.", _settings.Exchange);
        return _channel;
    }

    private void Reset()
    {
        try { _channel?.Dispose(); } catch (Exception) { }
        try { _connection?.Dispose(); } catch (Exception) { }
        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        Reset();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
