namespace SalesApi.Application.Services;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Seeds the default pipeline on first startup per organization (AC-1).
/// Runs as a hosted service so it runs before the app accepts requests.
/// If any pipeline already exists, skips seeding to keep the seeder idempotent.
/// </summary>
public class SalesSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<SalesSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        // Only seed if there are no pipelines at all (single-org MVP assumption)
        var anyPipeline = await context.Pipelines.AnyAsync(cancellationToken);
        if (anyPipeline)
        {
            logger.LogDebug("SalesSeeder: pipelines already exist; skipping seed.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        // Use a well-known placeholder org ID; real org IDs are set by the identity service.
        // The API's startup seeder creates pipelines via the create endpoint in production.
        // For dev/test, use the nil UUID as a placeholder.
        var orgId = Guid.Empty;

        var pipeline = new Pipeline
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Sales",
            IsDefault = true,
            CreatedAt = now,
            UpdatedAt = now,
            Stages = new List<PipelineStage>
            {
                new() { Id = Guid.NewGuid(), Name = "Qualified",       SortOrder = 1, Probability = 10,  StageType = "open", IsActive = true },
                new() { Id = Guid.NewGuid(), Name = "Needs analysis",  SortOrder = 2, Probability = 25,  StageType = "open", IsActive = true },
                new() { Id = Guid.NewGuid(), Name = "Proposal",        SortOrder = 3, Probability = 50,  StageType = "open", IsActive = true },
                new() { Id = Guid.NewGuid(), Name = "Negotiation",     SortOrder = 4, Probability = 75,  StageType = "open", IsActive = true },
                new() { Id = Guid.NewGuid(), Name = "Won",             SortOrder = 5, Probability = 100, StageType = "won",  IsActive = true },
                new() { Id = Guid.NewGuid(), Name = "Lost",            SortOrder = 6, Probability = 0,   StageType = "lost", IsActive = true }
            }
        };

        // Set PipelineId on each stage
        foreach (var stage in pipeline.Stages)
            stage.PipelineId = pipeline.Id;

        context.Pipelines.Add(pipeline);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("SalesSeeder: created default pipeline '{Name}' with {Count} stage(s).",
                pipeline.Name, pipeline.Stages.Count);
        }
        catch (Exception ex)
        {
            // On multi-instance startup race: another instance may have already seeded
            logger.LogWarning(ex, "SalesSeeder: seed failed (may be a race condition on first start).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
