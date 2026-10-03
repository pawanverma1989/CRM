namespace SalesApi.Workers;
using SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Placeholder background service that periodically logs stale deals (BRD-5).
/// The stale flag is computed on read (last_activity_at / stage_entered_at thresholds);
/// this worker only reports — it does not write any data.
/// </summary>
public class StaleDealsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SalesSettings> settings,
    ILogger<StaleDealsWorker> logger) : BackgroundService
{
    private readonly SalesSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once per day alongside the purge check
        var interval = TimeSpan.FromHours(Math.Max(1, _settings.PurgeIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportStaleDealsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stale deals worker failed.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReportStaleDealsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var now = DateTimeOffset.UtcNow;
        var activityCutoff = now.AddDays(-_settings.StaleDaysWithoutActivity);
        var stageCutoff = now.AddDays(-_settings.StaleDaysInStage);

        var staleByActivity = await context.Deals
            .CountAsync(d => d.Status == "open"
                          && d.DeletedAt == null
                          && d.LastActivityAt.HasValue
                          && d.LastActivityAt < activityCutoff, ct);

        var staleByStage = await context.Deals
            .CountAsync(d => d.Status == "open"
                          && d.DeletedAt == null
                          && d.StageEnteredAt < stageCutoff, ct);

        if (staleByActivity > 0 || staleByStage > 0)
        {
            logger.LogInformation(
                "Stale deals report: {ByActivity} stale by inactivity (>{Days1}d), {ByStage} stale by stage (>{Days2}d).",
                staleByActivity, _settings.StaleDaysWithoutActivity,
                staleByStage, _settings.StaleDaysInStage);
        }
        else
        {
            logger.LogDebug("Stale deals report: no stale deals found.");
        }
    }
}
