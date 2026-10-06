namespace LeadApi.Tests.Health;

using System;
using System.Text;
using FluentAssertions;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using LeadApi.Workers;
using Xunit;

public class EventConsumerDeadLetterTests
{
    private static BasicDeliverEventArgs Delivery(string? messageId, string body)
    {
        var properties = new Mock<IBasicProperties>();
        properties.SetupGet(p => p.MessageId).Returns(messageId!);
        return new BasicDeliverEventArgs(
            "consumer", 1, false, "crm.identity.events", "user.updated",
            properties.Object, Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public void EventIdOf_PrefersTheAmqpMessageId()
    {
        var id = Guid.NewGuid().ToString();

        EventConsumer.EventIdOf(Delivery(id, "{}")).Should().Be(id);
    }

    [Fact]
    public void EventIdOf_FallsBackToEnvelopeEventId()
    {
        var id = Guid.NewGuid();

        EventConsumer.EventIdOf(Delivery(null, $"{{\"event_id\":\"{id}\",\"payload\":{{\"email\":\"a@b.c\"}}}}"))
            .Should().Be(id.ToString());
    }

    [Fact]
    public void EventIdOf_WhenBodyIsUnreadable_ReturnsUnknown()
    {
        EventConsumer.EventIdOf(Delivery(null, "not json")).Should().Be("unknown");
    }
}
