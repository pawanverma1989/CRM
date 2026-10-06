namespace SalesApi.Workers;
using SalesApi.Application.Services;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs the nightly hard-delete of deals past their retention window (DEL-1).
/// </summary>
public class PurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerSettings> workers,
    IOptions<SalesSettings> settings,
    ILogger<PurgeWorker> logger) : BackgroundService
{
    private readonly WorkerSettings _workers = workers.Value;
    private readonly SalesSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.PurgeWorkerEnabled)
        {
            logger.LogInformation("Purge worker is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var purge = scope.ServiceProvider.GetRequiredService<IPurgeService>();
                await purge.PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purge worker failed.");
            }

            var interval = TimeSpan.FromHours(Math.Max(1, _settings.PurgeIntervalHours));
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
