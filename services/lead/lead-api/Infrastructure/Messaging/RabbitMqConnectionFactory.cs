namespace LeadApi.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

/// <summary>
/// Opens broker connections on demand. NFR-9: nothing in the request path depends on this, so a
/// broker outage leaves events waiting in <c>outbox_events</c> instead of failing a save.
/// </summary>
public interface IRabbitMqConnectionFactory
{
    /// <summary><c>null</c> when the broker cannot be reached right now.</summary>
    IConnection? TryConnect(out string? error);
}

public class RabbitMqConnectionFactory(IOptions<RabbitMqSettings> settings) : IRabbitMqConnectionFactory
{
    private readonly RabbitMqSettings _settings = settings.Value;

    public IConnection? TryConnect(out string? error)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.Host,
                Port = _settings.Port,
                UserName = _settings.Username,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                ClientProvidedName = "lead-api"
            };

            error = null;
            return factory.CreateConnection();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
