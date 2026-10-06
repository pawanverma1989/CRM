namespace LeadApi.Workers;
using System.Text;
using LeadApi.Application.Services;
using LeadApi.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

/// <summary>
/// Inbound event consumer. Declares the durable queue with a dead-letter exchange,
/// binds the routing keys, and hands every message to <see cref="IInboundEventProcessor"/>.
/// A message that keeps failing is dead-lettered (architecture §4.2).
/// </summary>
public class EventConsumer(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connections,
    IOptions<RabbitMqSettings> rabbit,
    IOptions<WorkerSettings> workers,
    ILogger<EventConsumer> logger) : BackgroundService
{
    private const string DeliveryAttemptHeader = "x-lead-delivery-attempt";

    private readonly RabbitMqSettings _settings = rabbit.Value;
    private readonly WorkerSettings _workers = workers.Value;

    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_workers.ConsumerEnabled)
        {
            logger.LogInformation("Inbound event consumer is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_channel is not { IsOpen: true } && !TryStart())
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        Reset();
    }

    private bool TryStart()
    {
        Reset();

        _connection = connections.TryConnect(out var error);
        if (_connection is null)
        {
            logger.LogWarning("Inbound consumer could not reach the broker ({Error}); retrying.", error);
            return false;
        }

        try
        {
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(_settings.DeadLetterExchange, ExchangeType.Fanout, durable: true);
            var deadLetterQueue = _settings.DeadLetterQueue;
            _channel.QueueDeclare(deadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(deadLetterQueue, _settings.DeadLetterExchange, routingKey: string.Empty);

            _channel.QueueDeclare(
                _settings.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = _settings.DeadLetterExchange
                });

            foreach (var exchange in _settings.ConsumeExchanges)
            {
                _channel.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true);
                foreach (var routingKey in _settings.ConsumeRoutingKeys)
                    _channel.QueueBind(_settings.Queue, exchange, routingKey);
            }

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += OnReceived;
            _channel.BasicConsume(_settings.Queue, autoAck: false, consumer);

            logger.LogInformation(
                "Inbound consumer listening on {Queue} for {Keys}.",
                _settings.Queue, string.Join(", ", _settings.ConsumeRoutingKeys));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inbound consumer could not set up its queue; retrying.");
            Reset();
            return false;
        }
    }

    private void OnReceived(object? sender, BasicDeliverEventArgs args)
    {
        var channel = _channel;
        if (channel is null) return;

        var attempt = Attempt(args);

        try
        {
            var body = Encoding.UTF8.GetString(args.Body.Span);
            var routingKey = args.BasicProperties?.Type ?? args.RoutingKey;

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IInboundEventProcessor>();

            var result = processor.ProcessAsync(body, routingKey, CancellationToken.None)
                                  .GetAwaiter().GetResult();

            logger.LogDebug("Inbound event {RoutingKey} was {Result}.", routingKey, result);
            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            if (attempt >= _settings.MaxDeliveryAttempts)
            {
                logger.LogError(ex,
                    "Giving up on inbound event {RoutingKey} after {Attempts} attempt(s); dead-lettering it.",
                    args.RoutingKey, attempt);
                logger.LogWarning(
                    "Dead-lettered inbound event {EventId} of type {EventType} to {DeadLetterQueue} after {Attempts} attempt(s).",
                    EventIdOf(args), args.BasicProperties?.Type ?? args.RoutingKey, _settings.DeadLetterQueue, attempt);
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            logger.LogWarning(ex,
                "Inbound event {RoutingKey} failed on attempt {Attempt}; requeueing.", args.RoutingKey, attempt);

            Republish(channel, args, attempt + 1);
            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
    }

    private void Republish(IModel channel, BasicDeliverEventArgs args, int attempt)
    {
        try
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = args.BasicProperties?.ContentType ?? "application/json";
            properties.MessageId = args.BasicProperties?.MessageId;
            properties.Type = args.BasicProperties?.Type ?? args.RoutingKey;
            properties.Headers = new Dictionary<string, object> { [DeliveryAttemptHeader] = attempt };

            channel.BasicPublish(
                exchange: string.Empty,
                routingKey: _settings.Queue,
                basicProperties: properties,
                body: args.Body.ToArray());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not requeue a failed inbound event; it will be redelivered on reconnect.");
        }
    }

    /// <summary>
    /// The event id for logs: the AMQP message id (the relay sets it to <c>event_id</c>), else the
    /// envelope's <c>event_id</c>. Reads nothing else from the body, so no personal data is logged.
    /// </summary>
    public static string EventIdOf(BasicDeliverEventArgs args)
    {
        var messageId = args.BasicProperties?.MessageId;
        if (!string.IsNullOrWhiteSpace(messageId)) return messageId;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(args.Body);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("event_id", out var id)
                && id.ValueKind == System.Text.Json.JsonValueKind.String
                && Guid.TryParse(id.GetString(), out var parsed))
                return parsed.ToString();
        }
        catch (System.Text.Json.JsonException) { }

        return "unknown";
    }

    private static int Attempt(BasicDeliverEventArgs args)
    {
        if (args.BasicProperties?.Headers is not { } headers) return 1;
        if (!headers.TryGetValue(DeliveryAttemptHeader, out var raw)) return 1;

        return raw switch
        {
            int value => value,
            long value => (int)value,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 1
        };
    }

    private void Reset()
    {
        try { _channel?.Dispose(); } catch (Exception) { }
        try { _connection?.Dispose(); } catch (Exception) { }
        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        Reset();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
