namespace LeadApi.Tests.Health;

using System;
using FluentAssertions;
using LeadApi.Infrastructure.Health;
using Xunit;

public class OutboxLagWarningThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static OutboxLagWarningThrottle Create()
        => new(threshold: TimeSpan.FromSeconds(60), interval: TimeSpan.FromMinutes(1));

    [Fact]
    public void ShouldWarn_WhenOldestRowIsWithinThreshold_ReturnsFalse()
    {
        Create().ShouldWarn(oldestOccurredAt: T0.AddSeconds(-30), now: T0).Should().BeFalse();
    }

    [Fact]
    public void ShouldWarn_WhenOldestRowExceedsThreshold_ReturnsTrue()
    {
        Create().ShouldWarn(oldestOccurredAt: T0.AddSeconds(-61), now: T0).Should().BeTrue();
    }

    [Fact]
    public void ShouldWarn_IsThrottledToOncePerMinute()
    {
        var throttle = Create();
        var oldest = T0.AddMinutes(-5);

        throttle.ShouldWarn(oldest, T0).Should().BeTrue();
        throttle.ShouldWarn(oldest, T0.AddSeconds(30)).Should().BeFalse();
        throttle.ShouldWarn(oldest, T0.AddSeconds(59)).Should().BeFalse();
        throttle.ShouldWarn(oldest, T0.AddSeconds(60)).Should().BeTrue();
    }

    [Fact]
    public void ShouldWarn_WhenLagClears_DoesNotConsumeTheThrottleWindow()
    {
        var throttle = Create();

        throttle.ShouldWarn(T0.AddSeconds(-10), T0).Should().BeFalse();
        throttle.ShouldWarn(T0.AddMinutes(-2), T0.AddSeconds(1)).Should().BeTrue();
    }
}
