namespace CustomerApi.Workers;
using CustomerApi.Application.Services;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// DEL-3: the nightly purge. It reclaims space; the recycle-bin rule itself is enforced by the
/// query filters, so a late run never makes an expired record look restorable (AC-15).
/// </summary>
public class PurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CustomerSettings> settings,
    IOptions<WorkerSettings> workers,
    ILogger<PurgeWorker> logger) : BackgroundService
{
    private readonly CustomerSettings _settings = settings.Value;
    private readonly WorkerSettings _workers = workers.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.PurgeEnabled)
        {
            logger.LogInformation("Retention purge is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _settings.PurgeIntervalHours));

        // Let the API finish starting before the first pass.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var purge = scope.ServiceProvider.GetRequiredService<IPurgeService>();
                await purge.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The retention purge pass failed; it will run again in {Hours} h.",
                    interval.TotalHours);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
