using Eventuous.Producers;
using Eventuous.Producers.Diagnostics;

namespace Eventuous.Azure.ServiceBus.Producers;

/// <summary>
/// Represents a producer for sending messages to Azure Service Bus.
/// </summary>
public class ServiceBusProducer : BaseProducer<ServiceBusProduceOptions>, IHostedProducer, IAsyncDisposable
{
    // maybe want something a bit more focused on Azure Service Bus?
    static readonly ProducerTracingOptions TracingOptions = new() { MessagingSystem = "azure-service-bus", DestinationKind = "topic", ProduceOperation = "publish" };
    private readonly ServiceBusProducerOptions options;
    private readonly ILogger<ServiceBusProducer>? log;
    private readonly ServiceBusSender sender;
    private readonly IEventSerializer serializer;
    private readonly ServiceBusMessageBatchBuilder messageBatchBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusProducer"/> class.
    /// This constructor sets up the Service Bus sender and prepares it for sending messages.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="options"></param>
    /// <param name="serializer"></param>
    /// <param name="log"></param>
    public ServiceBusProducer(
        ServiceBusClient client,
        ServiceBusProducerOptions options,
        IEventSerializer? serializer = null,
        ILogger<ServiceBusProducer>? log = null) : base(TracingOptions)
    {
        this.options = options;
        this.log = log;
        this.sender = client.CreateSender(options.QueueOrTopicName, options.SenderOptions);
        this.serializer = serializer ?? DefaultEventSerializer.Instance;
        this.messageBatchBuilder = new ServiceBusMessageBatchBuilder(sender, this.serializer, options.Attributes, SetActivityMessageType);
        log?.LogInformation("ServiceBusProducer created for {QueueOrTopicName}", options.QueueOrTopicName);
    }

    /// <summary>
    /// Checks if the producer is ready to send messages.
    /// </summary>
    public bool Ready { get; private set; }

    /// <summary>
    /// Disposes the Service Bus sender and releases resources.
    /// </summary>
    /// <returns></returns>
    public async ValueTask DisposeAsync()
    {
        await sender.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Starts the producer and prepares it for sending messages.
    /// The sender is actually created in the constructor, so this method is primarily for logging and readiness.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Ready = true;
        log?.LogInformation("ServiceBusProducer started for {QueueOrTopicName}", options.QueueOrTopicName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the producer and releases resources.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Ready = false;
        await sender.CloseAsync(cancellationToken);
        log?.LogInformation("ServiceBusProducer stopped for {QueueOrTopicName}", options.QueueOrTopicName);
    }

    /// <summary>
    /// Actually sends the messages to the Service Bus queue or topic.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="messages"></param>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task ProduceMessages(StreamName stream, IEnumerable<ProducedMessage> messages, ServiceBusProduceOptions? options, CancellationToken cancellationToken = default)
    {
        if (messages is ProducedMessage[] singleMessage && singleMessage.Length == 1)
        {
            await ProcessSingleMessage(stream, options, singleMessage, cancellationToken);
        }
        else
        {
            await ProcessMessagesInBatches(stream, messages, options, cancellationToken);
        }
    }

    private async Task ProcessSingleMessage(StreamName stream, ServiceBusProduceOptions? options, ProducedMessage[] singleMessage, CancellationToken cancellationToken)
    {
        var message = singleMessage[0];
        var serviceBusMessage = new ServiceBusMessageBuilder(
            serializer,
            stream,
            this.options.Attributes,
            options,
            SetActivityMessageType
        ).CreateServiceBusMessage(message);

        log?.LogInformation("Sending single message to {QueueOrTopicName}", this.options.QueueOrTopicName);
        try
        {
            await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
            await message.Ack<ServiceBusProducer>();
            log?.LogInformation("Single message sent successfully to {QueueOrTopicName}", this.options.QueueOrTopicName);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Failed to send single message to {QueueOrTopicName}", this.options.QueueOrTopicName);
            await message.Nack<ServiceBusProducer>("Failed to send single message", ex);
        }
        return;
    }

    private async Task ProcessMessagesInBatches(StreamName stream, IEnumerable<ProducedMessage> messages, ServiceBusProduceOptions? options, CancellationToken cancellationToken)
    {
        await foreach (var (batch, produced) in messageBatchBuilder.CreateMessageBatches(messages, stream, options, cancellationToken))
        {
            log?.LogInformation("Sending batch of {MessageCount} messages to {QueueOrTopicName}", batch.Count, this.options.QueueOrTopicName);
            try
            {
                await sender.SendMessagesAsync(batch, cancellationToken);
                foreach (var message in produced)
                {
                    await message.Ack<ServiceBusProducer>();
                }
                log?.LogInformation("Batch sent successfully to {QueueOrTopicName}", this.options.QueueOrTopicName);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Failed to send batch to {QueueOrTopicName}", this.options.QueueOrTopicName);
                foreach (var message in produced)
                {
                    await message.Nack<ServiceBusProducer>("Failed to send batch", ex);
                }
            }
        }
    }
}
