namespace LeadApi.Infrastructure.Health;
using LeadApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>How far the outbox relay is behind: unpublished rows and the age of the oldest one.</summary>
public readonly record struct OutboxLag(int UnpublishedCount, TimeSpan OldestAge)
{
    public static readonly OutboxLag None = new(0, TimeSpan.Zero);

    /// <summary>Oldest age in whole-ish seconds, for health data and log fields.</summary>
    public double OldestAgeSeconds => Math.Round(OldestAge.TotalSeconds, 1);

    /// <summary>Reads counts and timestamps only; never the payload (which may hold personal data).</summary>
    public static async Task<OutboxLag> MeasureAsync(
        IQueryable<OutboxEvent> outbox, DateTimeOffset now, CancellationToken ct)
    {
        var unpublished = outbox.AsNoTracking().Where(e => e.PublishedAt == null);

        var oldest = await unpublished
            .OrderBy(e => e.OccurredAt)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(ct);

        if (oldest is null) return None;

        var count = await unpublished.CountAsync(ct);
        var age = now - oldest.Value;

        return new OutboxLag(count, age < TimeSpan.Zero ? TimeSpan.Zero : age);
    }
}

/// <summary>
/// Decides when the relay should warn about outbox lag: only when the oldest unpublished row is
/// older than the threshold, and at most once per <c>interval</c>.
/// </summary>
public sealed class OutboxLagWarningThrottle(TimeSpan threshold, TimeSpan interval)
{
    private DateTimeOffset? _lastWarning;

    public bool ShouldWarn(DateTimeOffset oldestOccurredAt, DateTimeOffset now)
    {
        if (now - oldestOccurredAt <= threshold) return false;
        if (_lastWarning is { } last && now - last < interval) return false;

        _lastWarning = now;
        return true;
    }
}
