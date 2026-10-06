namespace IdentityApi.Tests.Infrastructure;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Infrastructure.Messaging;
using IdentityApi.Infrastructure.Repositories;
using IdentityApi.Settings;
using IdentityApi.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

public class OutboxRelayTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IModel> _channel = new();
    private readonly Mock<IConnection> _connection = new();
    private readonly List<(string Exchange, string RoutingKey, IBasicProperties Props, string Body)> _published = [];

    public OutboxRelayTests()
    {
        _channel.SetupGet(c => c.IsOpen).Returns(true);
        _channel.Setup(c => c.CreateBasicProperties()).Returns(() => new FakeProperties());
        _channel.Setup(c => c.BasicPublish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<IBasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>()))
            .Callback<string, string, bool, IBasicProperties, ReadOnlyMemory<byte>>((ex, rk, _, p, body)
                => _published.Add((ex, rk, p, Encoding.UTF8.GetString(body.Span))));
        _connection.Setup(c => c.CreateModel()).Returns(_channel.Object);
    }

    private IServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        return services.BuildServiceProvider();
    }

    private OutboxRelay Relay(IServiceProvider sp, IConnection? connection)
    {
        var factory = new Mock<IRabbitMqConnectionFactory>();
        string? error = connection is null ? "down" : null;
        factory.Setup(f => f.TryConnect(out error)).Returns(connection);
        return new OutboxRelay(
            sp.GetRequiredService<IServiceScopeFactory>(),
            factory.Object,
            Options.Create(new RabbitMqSettings { Exchange = "crm.identity.events", RelayBatchSize = 100 }),
            Options.Create(new WorkerSettings()),
            TimeProvider.System,
            NullLogger<OutboxRelay>.Instance);
    }

    private static async Task<OutboxEvent> SeedAsync(IServiceProvider sp, string eventType, string payload)
    {
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = new OutboxEvent
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), AggregateType = "user", AggregateId = Guid.NewGuid(),
            EventType = eventType, Payload = payload, OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-5)
        };
        ctx.OutboxEvents.Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    private static async Task<OutboxEvent> ReloadAsync(IServiceProvider sp, Guid id)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task RunOnce_PublishesEnvelopeWithRoutingKeyAndMessageId_AndMarksRowPublished()
    {
        var sp = Services();
        var row = await SeedAsync(sp, "user.created", """{"user_id":"x","version":1}""");

        var published = await Relay(sp, _connection.Object).RunOnceAsync();

        published.Should().Be(1);
        var msg = _published.Should().ContainSingle().Subject;
        msg.Exchange.Should().Be("crm.identity.events");
        msg.RoutingKey.Should().Be("user.created");
        msg.Props.MessageId.Should().Be(row.Id.ToString());
        msg.Props.Persistent.Should().BeTrue();
        using var doc = JsonDocument.Parse(msg.Body);
        doc.RootElement.GetProperty("event_id").GetGuid().Should().Be(row.Id);
        doc.RootElement.GetProperty("organization_id").GetGuid().Should().Be(row.OrganizationId);
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        _channel.Verify(c => c.ConfirmSelect(), Times.Once);
        _channel.Verify(c => c.WaitForConfirmsOrDie(It.IsAny<TimeSpan>()), Times.Once);
        (await ReloadAsync(sp, row.Id)).PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_ConfirmFails_LeavesRowUnpublishedAndCountsAttempt()
    {
        _channel.Setup(c => c.WaitForConfirmsOrDie(It.IsAny<TimeSpan>())).Throws(new IOException("nack"));
        var sp = Services();
        var row = await SeedAsync(sp, "user.updated", "{}");

        var published = await Relay(sp, _connection.Object).RunOnceAsync();

        published.Should().Be(0);
        var reloaded = await ReloadAsync(sp, row.Id);
        reloaded.PublishedAt.Should().BeNull();
        reloaded.PublishAttempts.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_BrokerDown_LeavesRowsWaiting()
    {
        var sp = Services();
        var row = await SeedAsync(sp, "user.updated", "{}");

        var published = await Relay(sp, connection: null).RunOnceAsync();

        published.Should().Be(0);
        (await ReloadAsync(sp, row.Id)).PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunOnce_AlreadyPublishedRows_AreNotRepublished()
    {
        var sp = Services();
        var row = await SeedAsync(sp, "user.updated", "{}");
        var relay = Relay(sp, _connection.Object);

        await relay.RunOnceAsync();
        await relay.RunOnceAsync();

        _published.Should().ContainSingle(m => m.Props.MessageId == row.Id.ToString());
    }

    /// <summary>Minimal IBasicProperties so the test can read back what the relay set.</summary>
    private sealed class FakeProperties : IBasicProperties
    {
        public ushort ProtocolClassId => 60;
        public string ProtocolClassName => "basic";
        public string AppId { get; set; } = null!;
        public string ClusterId { get; set; } = null!;
        public string ContentEncoding { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public string CorrelationId { get; set; } = null!;
        public byte DeliveryMode { get; set; }
        public string Expiration { get; set; } = null!;
        public IDictionary<string, object> Headers { get; set; } = null!;
        public string MessageId { get; set; } = null!;
        public bool Persistent { get => DeliveryMode == 2; set => DeliveryMode = value ? (byte)2 : (byte)1; }
        public byte Priority { get; set; }
        public string ReplyTo { get; set; } = null!;
        public PublicationAddress ReplyToAddress { get; set; } = null!;
        public AmqpTimestamp Timestamp { get; set; }
        public string Type { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public void ClearAppId() { }
        public void ClearClusterId() { }
        public void ClearContentEncoding() { }
        public void ClearContentType() { }
        public void ClearCorrelationId() { }
        public void ClearDeliveryMode() { }
        public void ClearExpiration() { }
        public void ClearHeaders() { }
        public void ClearMessageId() { }
        public void ClearPriority() { }
        public void ClearReplyTo() { }
        public void ClearTimestamp() { }
        public void ClearType() { }
        public void ClearUserId() { }
        public bool IsAppIdPresent() => AppId != null;
        public bool IsClusterIdPresent() => ClusterId != null;
        public bool IsContentEncodingPresent() => ContentEncoding != null;
        public bool IsContentTypePresent() => ContentType != null;
        public bool IsCorrelationIdPresent() => CorrelationId != null;
        public bool IsDeliveryModePresent() => DeliveryMode != 0;
        public bool IsExpirationPresent() => Expiration != null;
        public bool IsHeadersPresent() => Headers != null;
        public bool IsMessageIdPresent() => MessageId != null;
        public bool IsPriorityPresent() => Priority != 0;
        public bool IsReplyToPresent() => ReplyTo != null;
        public bool IsTimestampPresent() => true;
        public bool IsTypePresent() => Type != null;
        public bool IsUserIdPresent() => UserId != null;
    }
}
