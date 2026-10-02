namespace LeadApi.Settings;

/// <summary>Broker settings. Override individually as <c>RabbitMq__Host</c>, <c>RabbitMq__Password</c>, …</summary>
public class RabbitMqSettings
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "crm_user";
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange this service publishes to; routing key is the event type.</summary>
    public string Exchange { get; set; } = "crm.lead.events";

    /// <summary>Queue this service consumes from.</summary>
    public string Queue { get; set; } = "crm.lead.inbox";

    /// <summary>Exchanges this service consumes from.</summary>
    public string[] ConsumeExchanges { get; set; } = [];

    /// <summary>Routing keys bound to <see cref="Queue"/>.</summary>
    public string[] ConsumeRoutingKeys { get; set; } = [];

    public string DeadLetterExchange { get; set; } = "crm.lead.dlx";

    /// <summary>Attempts before a consumed message is dead-lettered.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Outbox relay poll interval.</summary>
    public int RelayPollSeconds { get; set; } = 5;

    /// <summary>Outbox rows published per relay pass.</summary>
    public int RelayBatchSize { get; set; } = 50;
}

/// <summary>
/// Lets integration tests host the API without a broker, purge timer, or conversion retry worker running.
/// </summary>
public class WorkerSettings
{
    public bool OutboxRelayEnabled { get; set; } = true;
    public bool ConsumerEnabled { get; set; } = true;
    public bool ConversionRetryEnabled { get; set; } = true;
    public bool PurgeEnabled { get; set; } = true;
}
