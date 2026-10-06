namespace IdentityApi.Settings;

/// <summary>
/// Broker settings. Override individually as <c>RabbitMq__Host</c>, <c>RabbitMq__Port</c>,
/// <c>RabbitMq__Username</c>, <c>RabbitMq__Password</c>, <c>RabbitMq__Exchange</c>, …
/// Identity only publishes; it consumes nothing.
/// </summary>
public class RabbitMqSettings
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "crm_user";
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange this service publishes to; routing key is the event type.</summary>
    public string Exchange { get; set; } = "crm.identity.events";

    /// <summary>Outbox relay poll interval.</summary>
    public int RelayPollSeconds { get; set; } = 5;

    /// <summary>Outbox rows published per relay pass.</summary>
    public int RelayBatchSize { get; set; } = 100;

    /// <summary>
    /// When the oldest unpublished outbox row is older than this, the relay logs a warning (at most
    /// once a minute) and <c>/health/events</c> reports Degraded.
    /// </summary>
    public int OutboxLagWarningSeconds { get; set; } = 60;
}

/// <summary>Lets tests and local runs host the API without a broker.</summary>
public class WorkerSettings
{
    public bool OutboxRelayEnabled { get; set; } = true;
}
