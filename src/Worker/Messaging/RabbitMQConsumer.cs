using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Worker.Data;
using Worker.Models;
using Worker.Services;

namespace Worker.Messaging;

/// <summary>
/// Background service that continuously listens to the RabbitMQ queue
/// and processes incoming PDF extraction jobs.
///
/// Key design decisions:
/// - prefetchCount=1: processes one message at a time to avoid overloading the DB
///   and to ensure each PDF is fully processed before taking the next one.
/// - Manual acknowledgement (autoAck=false): message is ACKed only after successful
///   DB save, guaranteeing at-least-once processing semantics.
/// - On failure: message is NACKed and requeued once. If it fails again on redelivery,
///   it is discarded (dead-lettered) to prevent an infinite retry loop.
/// </summary>
public class RabbitMQConsumerService : BackgroundService
{
    private const string QueueName = "pdf.processing";

    // IServiceScopeFactory is used to create a new DI scope per message,
    // because AppDbContext is registered as Scoped (not Singleton).
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMQConsumerService> _logger;

    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMQConsumerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<RabbitMQConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RabbitMQ consumer starting...");

        // Retry connection on startup — RabbitMQ may not be ready yet in Docker.
        await ConnectWithRetryAsync(stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.Received += OnMessageReceivedAsync;

        _channel!.BasicConsume(
            queue: QueueName,
            autoAck: false,   // Manual ACK — we confirm only after successful processing
            consumer: consumer
        );

        _logger.LogInformation("Listening on queue '{Queue}'", QueueName);

        // Keep the service alive until the host signals shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// Attempts to connect to RabbitMQ with exponential-like retry.
    /// Necessary because the Worker container may start before RabbitMQ is ready.
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:Host"] ?? "rabbitmq",
            Port = int.Parse(_config["RabbitMQ:Port"] ?? "5672"),
            UserName = _config["RabbitMQ:Username"] ?? "guest",
            Password = _config["RabbitMQ:Password"] ?? "guest",
            VirtualHost = _config["RabbitMQ:VirtualHost"] ?? "/",
            DispatchConsumersAsync = true
        };

        var retries = 10;
        while (retries-- > 0)
        {
            try
            {
                _connection = factory.CreateConnection("pdf-worker-consumer");
                _channel = _connection.CreateModel();

                // Declare the queue here too — ensures it exists even if the API
                // Gateway hasn't published anything yet.
                _channel.QueueDeclare(
                    queue: QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                // Only fetch one message at a time.
                // Prevents the Worker from pulling all messages at once
                // and blocking other potential consumer instances.
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                _logger.LogInformation("Connected to RabbitMQ at {Host}", factory.HostName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ not ready ({Retries} retries left): {Message}", retries, ex.Message);
                await Task.Delay(3000, ct);
            }
        }

        throw new Exception("Failed to connect to RabbitMQ after multiple retries.");
    }

    /// <summary>
    /// Called for each message received from the queue.
    /// Deserializes the message and delegates to <see cref="ProcessDocumentAsync"/>.
    /// Handles ACK/NACK based on success or failure.
    /// </summary>
    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var deliveryTag = ea.DeliveryTag;
        ProcessingMessage? message = null;

        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            message = JsonSerializer.Deserialize<ProcessingMessage>(body);

            if (message is null)
            {
                _logger.LogError("Failed to deserialize message body: {Body}", body);
                // Discard the malformed message — requeueing would loop forever
                _channel!.BasicNack(deliveryTag, multiple: false, requeue: false);
                return;
            }

            _logger.LogInformation("Processing document {DocumentId} ('{OriginalName}')",
                message.DocumentId, message.OriginalName);

            await ProcessDocumentAsync(message);

            // ACK tells RabbitMQ the message was handled successfully and can be removed from the queue
            _channel!.BasicAck(deliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for document {DocumentId}", message?.DocumentId);

            // Requeue once on first failure. If it was already redelivered and failed again,
            // discard it to prevent an infinite retry loop.
            var requeue = !ea.Redelivered;
            _channel!.BasicNack(deliveryTag, multiple: false, requeue: requeue);
        }
    }

    /// <summary>
    /// Core processing logic:
    /// 1. Load the document record from the DB
    /// 2. Update status to Processing
    /// 3. Extract text using IPdfExtractor
    /// 4. Save results and update status to Completed or Failed
    /// </summary>
    private async Task ProcessDocumentAsync(ProcessingMessage message)
    {
        // Create a new DI scope for this unit of work (required for scoped DbContext)
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPdfExtractor>();

        var document = await db.Documents.FindAsync(message.DocumentId);
        if (document is null)
        {
            _logger.LogError("Document {DocumentId} not found in the database", message.DocumentId);
            return;
        }

        // Mark as in-progress so the client can observe the intermediate state
        document.Status = DocumentStatus.Processing;
        document.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            if (!File.Exists(message.FilePath))
                throw new FileNotFoundException($"PDF file not found at path: {message.FilePath}");

            var result = extractor.Extract(message.FilePath);

            document.ExtractedText = result.Text;
            document.PageCount = result.PageCount;
            document.Status = DocumentStatus.Completed;
            document.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Document {DocumentId} processed: {Pages} pages, {Chars} characters extracted",
                message.DocumentId, result.PageCount, result.Text.Length
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from document {DocumentId}", message.DocumentId);

            // Persist the failure reason so the client can inspect it via the API
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            document.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
