using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace ApiGateway.Messaging;

/// <summary>
/// Abstraction over the message broker.
/// Allows swapping RabbitMQ for another transport without changing business logic.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string queueName, CancellationToken ct = default);
}

/// <summary>
/// RabbitMQ implementation of <see cref="IMessagePublisher"/>.
/// Registered as a singleton — a single connection is reused for the lifetime of the app.
/// </summary>
public class RabbitMQPublisher : IMessagePublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMQPublisher> _logger;

    public RabbitMQPublisher(IConfiguration config, ILogger<RabbitMQPublisher> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMQ:Host"] ?? "rabbitmq",
            Port = int.Parse(config["RabbitMQ:Port"] ?? "5672"),
            UserName = config["RabbitMQ:Username"] ?? "guest",
            Password = config["RabbitMQ:Password"] ?? "guest",
            VirtualHost = config["RabbitMQ:VirtualHost"] ?? "/",
            // Required for async consumer callbacks on the Worker side.
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection("api-gateway-publisher");
        _channel = _connection.CreateModel();

        _logger.LogInformation("RabbitMQ publisher connected to {Host}", factory.HostName);
    }

    /// <summary>
    /// Serializes <paramref name="message"/> to JSON and publishes it to the specified queue.
    /// The queue is declared as durable so messages survive a broker restart.
    /// Messages are marked persistent (delivery mode 2) for the same reason.
    /// </summary>
    public Task PublishAsync<T>(T message, string queueName, CancellationToken ct = default)
    {
        // Idempotent — safe to call even if the queue already exists.
        _channel.QueueDeclare(
            queue: queueName,
            durable: true,       // Queue survives broker restart
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;                // Message survives broker restart
        props.ContentType = "application/json";
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange: string.Empty,   // Default exchange — routes directly by queue name
            routingKey: queueName,
            basicProperties: props,
            body: body
        );

        _logger.LogInformation("Published message to queue '{Queue}': {@Message}", queueName, message);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
