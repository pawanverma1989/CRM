namespace LeadApi.Tests.TestSupport;

using System;

/// <summary>A clock that always reads the same instant, so age calculations are deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
