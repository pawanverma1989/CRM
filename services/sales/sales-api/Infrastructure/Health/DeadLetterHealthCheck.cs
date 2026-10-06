namespace SalesApi.Infrastructure.Health;
using SalesApi.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

/// <summary>
/// <c>sales-dead-letters</c> (tag <c>events</c>): Degraded when the consumer's dead-letter queue
/// holds any message, or when the broker cannot be reached. Never throws: it opens a short-lived
/// connection and channel, does a passive declare to read the message count, and disposes both.
/// </summary>
public class DeadLetterHealthCheck(
    IRabbitMqConnectionFactory connections,
    IOptions<RabbitMqSettings> rabbit) : IHealthCheck
{
    /// <summary>Upper bound on waiting for the broker, so a black-holed host cannot stall the probe.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public const string BrokerUnreachable = "broker unreachable";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var queue = rabbit.Value.DeadLetterQueue;
        var data = new Dictionary<string, object> { ["queue"] = queue };

        var connection = await ConnectAsync(cancellationToken);
        if (connection is null)
            return HealthCheckResult.Degraded(BrokerUnreachable, data: data);

        try
        {
            using var channel = connection.CreateModel();
            long count = channel.QueueDeclarePassive(queue).MessageCount;
            data["dead_letter_count"] = count;

            return count > 0
                ? HealthCheckResult.Degraded($"{count} message(s) in the dead-letter queue", data: data)
                : HealthCheckResult.Healthy("dead-letter queue is empty", data);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            // The consumer has not declared it yet, so nothing can have been dead-lettered.
            data["dead_letter_count"] = 0L;
            return HealthCheckResult.Healthy("dead-letter queue not declared yet", data);
        }
        catch (Exception)
        {
            return HealthCheckResult.Degraded(BrokerUnreachable, data: data);
        }
        finally
        {
            try { connection.Dispose(); } catch (Exception) { /* closing a broken connection is not news */ }
        }
    }

    private async Task<IConnection?> ConnectAsync(CancellationToken ct)
    {
        var connect = Task.Run(() => connections.TryConnect(out _), CancellationToken.None);

        try
        {
            return await connect.WaitAsync(ConnectTimeout, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Dispose the connection if it turns up after we stopped waiting.
            _ = connect.ContinueWith(
                t => { try { t.Result?.Dispose(); } catch (Exception) { } },
                CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

            if (ex is OperationCanceledException) throw;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
