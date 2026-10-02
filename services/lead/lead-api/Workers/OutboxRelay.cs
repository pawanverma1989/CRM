namespace LeadApi.Workers;
using System.Text;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

/// <summary>
/// Publishes unpublished <c>outbox_events</c> rows to the broker topic exchange
/// (CLAUDE.md rule 3). At-least-once delivery — consumers deduplicate via processed_events.
/// With the broker down it keeps retrying; writes are unaffected (NFR-9).
/// </summary>
public class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connections,
    IOptions<RabbitMqSettings> rabbit,
    IOptions<WorkerSettings> workers,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private readonly RabbitMqSettings _settings = rabbit.Value;
    private readonly WorkerSettings _workers = workers.Value;

    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.OutboxRelayEnabled)
        {
            logger.LogInformation("Outbox relay is disabled by configuration.");
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, _settings.RelayPollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;

            try
            {
                published = await PublishBatchAsync(stoppingToken);
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

            if (published < _settings.RelayBatchSize)
            {
                try { await Task.Delay(poll, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        Reset();
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeadDbContext>();

        var batch = await context.OutboxEvents
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.OccurredAt)
            .Take(_settings.RelayBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        var channel = EnsureChannel();
        if (channel is null) return 0;

        var now = DateTimeOffset.UtcNow;
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
                    body: Encoding.UTF8.GetBytes(row.Payload));
            }

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));

            foreach (var row in batch) row.PublishedAt = now;
            published = batch.Count;
        }
        catch (Exception ex)
        {
            foreach (var row in batch) row.PublishAttempts += 1;
            Reset();
            logger.LogWarning(ex,
                "Could not publish {Count} outbox row(s); they stay unpublished and will be retried.",
                batch.Count);
        }

        await context.SaveChangesAsync(ct);
        return published;
    }

    private IModel? EnsureChannel()
    {
        if (_channel is { IsOpen: true }) return _channel;

        Reset();

        _connection = connections.TryConnect(out var error);
        if (_connection is null)
        {
            logger.LogWarning(
                "The broker is unreachable ({Error}); events are waiting in the outbox.", error);
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
