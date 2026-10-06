namespace CustomerApi.Tests.Unit.Health;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using CustomerApi.Infrastructure.Health;
using CustomerApi.Infrastructure.Messaging;
using CustomerApi.Settings;
using Xunit;

public class DeadLetterHealthCheckTests
{
    private const string DeadLetterQueue = "crm.customer.inbox.dead";

    private static DeadLetterHealthCheck CreateCheck(IRabbitMqConnectionFactory factory)
        => new(factory, Options.Create(new RabbitMqSettings { Queue = "crm.customer.inbox" }));

    private static Task<HealthCheckResult> RunAsync(DeadLetterHealthCheck check)
        => check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    private static (Mock<IRabbitMqConnectionFactory> factory, Mock<IConnection> connection, Mock<IModel> channel)
        BrokerWithDeadLetters(uint count)
    {
        var channel = new Mock<IModel>();
        channel.Setup(c => c.QueueDeclarePassive(DeadLetterQueue))
               .Returns(new QueueDeclareOk(DeadLetterQueue, count, 0));

        var connection = new Mock<IConnection>();
        connection.Setup(c => c.CreateModel()).Returns(channel.Object);

        var factory = new Mock<IRabbitMqConnectionFactory>();
        string? error = null;
        factory.Setup(f => f.TryConnect(out error)).Returns(connection.Object);

        return (factory, connection, channel);
    }

    [Fact]
    public void DeadLetterQueue_IsTheConsumerQueueWithDeadSuffix()
    {
        new RabbitMqSettings { Queue = "crm.customer.inbox" }.DeadLetterQueue.Should().Be(DeadLetterQueue);
    }

    [Fact]
    public async Task CheckHealth_WhenBrokerIsUnreachable_ReturnsDegradedWithoutThrowing()
    {
        var factory = new Mock<IRabbitMqConnectionFactory>();
        string? error = "Connection refused";
        factory.Setup(f => f.TryConnect(out error)).Returns((IConnection?)null);

        var result = await RunAsync(CreateCheck(factory.Object));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("broker unreachable");
        result.Data["queue"].Should().Be(DeadLetterQueue);
    }

    [Fact]
    public async Task CheckHealth_WhenConnectionFactoryThrows_ReturnsDegradedBrokerUnreachable()
    {
        var factory = new Mock<IRabbitMqConnectionFactory>();
        string? error = null;
        factory.Setup(f => f.TryConnect(out error)).Throws(new InvalidOperationException("boom"));

        var result = await RunAsync(CreateCheck(factory.Object));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("broker unreachable");
    }

    [Fact]
    public async Task CheckHealth_WhenDeadLetterQueueHasMessages_ReturnsDegradedWithCount()
    {
        var (factory, _, _) = BrokerWithDeadLetters(3);

        var result = await RunAsync(CreateCheck(factory.Object));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["dead_letter_count"].Should().Be(3L);
        result.Data["queue"].Should().Be(DeadLetterQueue);
    }

    [Fact]
    public async Task CheckHealth_WhenDeadLetterQueueIsEmpty_ReturnsHealthy()
    {
        var (factory, _, _) = BrokerWithDeadLetters(0);

        var result = await RunAsync(CreateCheck(factory.Object));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["dead_letter_count"].Should().Be(0L);
    }

    [Fact]
    public async Task CheckHealth_DisposesTheShortLivedChannelAndConnection()
    {
        var (factory, connection, channel) = BrokerWithDeadLetters(0);

        await RunAsync(CreateCheck(factory.Object));

        channel.Verify(c => c.Dispose(), Times.Once);
        connection.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task CheckHealth_WhenPassiveDeclareFails_ReturnsDegradedWithoutThrowing()
    {
        var channel = new Mock<IModel>();
        channel.Setup(c => c.QueueDeclarePassive(It.IsAny<string>())).Throws(new InvalidOperationException("closed"));
        var connection = new Mock<IConnection>();
        connection.Setup(c => c.CreateModel()).Returns(channel.Object);
        var factory = new Mock<IRabbitMqConnectionFactory>();
        string? error = null;
        factory.Setup(f => f.TryConnect(out error)).Returns(connection.Object);

        var result = await RunAsync(CreateCheck(factory.Object));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("broker unreachable");
        connection.Verify(c => c.Dispose(), Times.Once);
    }
}
