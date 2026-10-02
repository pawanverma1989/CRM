namespace CustomerApi.Settings;

/// <summary>Broker settings. Override individually as <c>RabbitMq__Host</c>, <c>RabbitMq__Password</c>, …</summary>
public class RabbitMqSettings
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "crm_user";
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange this service publishes to; routing key is the event type.</summary>
    public string Exchange { get; set; } = "crm.customer.events";

    /// <summary>Exchanges this service consumes from (identity and compliance events, §6.2).</summary>
    public string[] ConsumeExchanges { get; set; } =
        ["crm.identity.events", "crm.compliance.events"];

    public string Queue { get; set; } = "crm.customer.inbox";

    /// <summary>Routing keys bound to <see cref="Queue"/>.</summary>
    public string[] ConsumeRoutingKeys { get; set; } =
        ["user.created", "user.updated", "user.deactivated", "dsr.erasure_requested", "dsr.access_requested"];

    public string DeadLetterExchange { get; set; } = "crm.customer.dlx";

    /// <summary>Attempts before a consumed message is dead-lettered.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Outbox relay poll interval.</summary>
    public int RelayPollSeconds { get; set; } = 2;

    /// <summary>Outbox rows published per relay pass.</summary>
    public int RelayBatchSize { get; set; } = 100;

    /// <summary>Relay back-off base; the delay grows with <c>publish_attempts</c>.</summary>
    public int RelayBackoffSeconds { get; set; } = 5;
}

/// <summary>
/// Lets integration tests host the API without a broker or a purge timer running.
/// </summary>
public class WorkerSettings
{
    public bool OutboxRelayEnabled { get; set; } = true;
    public bool ConsumerEnabled { get; set; } = true;
    public bool PurgeEnabled { get; set; } = true;
}
