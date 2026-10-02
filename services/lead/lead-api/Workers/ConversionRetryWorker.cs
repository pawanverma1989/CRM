namespace LeadApi.Workers;
using LeadApi.Application.Services;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Polls for pending lead_conversions and resumes them from where they left off (CNV-4).
/// A conversion is pending when its status is not completed/compensated/failed and
/// either next_retry_at is null or has passed, and attempts &lt; max_retries.
/// </summary>
public class ConversionRetryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerSettings> workers,
    IOptions<LeadSettings> leadSettings,
    ILogger<ConversionRetryWorker> logger) : BackgroundService
{
    private readonly WorkerSettings _workers = workers.Value;
    private readonly LeadSettings _leadSettings = leadSettings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.ConversionRetryEnabled)
        {
            logger.LogInformation("Conversion retry worker is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Conversion retry worker pass failed.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RetryPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var leadRepo = scope.ServiceProvider.GetRequiredService<ILeadRepository>();
        var conversionService = scope.ServiceProvider.GetRequiredService<IConversionService>();

        var pending = await leadRepo.ListPendingConversionsAsync(_leadSettings.MaxConversionRetries, ct);

        if (pending.Count == 0) return;

        logger.LogInformation("Conversion retry worker: resuming {Count} pending conversion(s).", pending.Count);

        foreach (var conversion in pending)
        {
            try
            {
                await conversionService.ResumeAsync(conversion, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversion retry worker failed to resume conversion {ConversionId}.", conversion.Id);
            }
        }
    }
}
