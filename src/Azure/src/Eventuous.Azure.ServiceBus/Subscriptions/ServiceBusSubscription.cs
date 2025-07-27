using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;
using Eventuous.Subscriptions.Filters;
using Eventuous.Subscriptions.Logging;

namespace Eventuous.Azure.ServiceBus.Subscriptions;

/// <summary>
/// Represents a Service Bus subscription that processes messages from a queue or topic.
/// </summary>
public class ServiceBusSubscription : EventSubscription<ServiceBusSubscriptionOptions>
{
    private readonly ServiceBusClient client;
    private readonly Func<ProcessErrorEventArgs, Task> defaultErrorHandler;
    private ServiceBusProcessor? processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusSubscription"/> class.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="options"></param>
    /// <param name="consumePipe"></param>
    /// <param name="loggerFactory"></param>
    /// <param name="eventSerializer"></param>
    public ServiceBusSubscription(ServiceBusClient client, ServiceBusSubscriptionOptions options, ConsumePipe consumePipe, ILoggerFactory? loggerFactory, IEventSerializer? eventSerializer) :
     base(options, consumePipe, loggerFactory, eventSerializer)
    {
        this.client = client;
        this.defaultErrorHandler = Options.ErrorHandler ?? DefaultErrorHandler;
    }

    /// <summary>
    /// Subscribes to the Service Bus queue or topic.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    protected override ValueTask Subscribe(CancellationToken cancellationToken)
    {
        processor = Options.QueueOrTopic.MakeProcessor(client, Options);
        processor.ProcessMessageAsync += HandleMessage;

        processor.ProcessErrorAsync += defaultErrorHandler;
        return new ValueTask(processor.StartProcessingAsync(cancellationToken));

        async Task HandleMessage(ProcessMessageEventArgs arg)
        {
            CancellationToken ct = arg.CancellationToken;
            if (ct.IsCancellationRequested) return;
            var msg = arg.Message;
            var eventType = msg.ApplicationProperties[Options.Attributes.EventType].ToString()
                ?? throw new InvalidOperationException("Event type is missing in message properties");
            var contentType = msg.ContentType;
            // Should this be a stream name? or topic or something
            var streamName = msg.ApplicationProperties[Options.Attributes.StreamName].ToString()
                ?? throw new InvalidOperationException("Stream name is missing in message properties");

            Logger.Current = Log;

            var evt = DeserializeData(contentType, eventType, msg.Body, streamName);

            var applicationProperties = msg.ApplicationProperties.Concat(MessageProperties(msg));
            var ctx = new MessageConsumeContext(
                msg.MessageId,
                eventType,
                contentType,
                streamName,
                0,
                0,
                0,
                Sequence++,
                msg.EnqueuedTime.UtcDateTime,
                evt,
                AsMeta(applicationProperties),
                SubscriptionId,
                ct
            );

            try
            {
                await Handler(ctx);
                await arg.CompleteMessageAsync(msg, ct);
            }
            catch (Exception ex)
            {
                await arg.AbandonMessageAsync(msg, null, ct); // Abandoning the message will make it available for reprocessing, or dead letter it?
                await defaultErrorHandler(new(ex, ServiceBusErrorSource.Abandon, arg.FullyQualifiedNamespace, arg.EntityPath, arg.Identifier, arg.CancellationToken));
                Log.ErrorLog?.Log(ex, "Error processing message: {MessageId}", msg.MessageId);
            }
        }
    }

    private IEnumerable<KeyValuePair<string, object>> MessageProperties(ServiceBusReceivedMessage msg)
    {
        var attributes = Options.Attributes;
        if (msg.CorrelationId is not null)
            yield return new KeyValuePair<string, object>(attributes.CorrelationId, msg.CorrelationId);
        if (msg.ReplyTo is not null)
            yield return new KeyValuePair<string, object>(attributes.ReplyTo, msg.ReplyTo);
        if (msg.Subject is not null)
            yield return new KeyValuePair<string, object>(attributes.Subject, msg.Subject);
        if (msg.To is not null)
            yield return new KeyValuePair<string, object>(attributes.To, msg.To);
        if (msg.MessageId is not null)
            yield return new KeyValuePair<string, object>(attributes.MessageId, msg.MessageId);
    }

    private static Metadata? AsMeta(IEnumerable<KeyValuePair<string, object>> applicationProperties) =>
        new(applicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));

    private async Task DefaultErrorHandler(ProcessErrorEventArgs arg)
    {
        // Log the error
        Log.ErrorLog?.Log(arg.Exception, "Error processing message: {Identifier}", arg.Identifier);

        // Optionally, you can handle the error further, e.g., by sending to a dead-letter queue
        await Task.CompletedTask;
    }

    /// <summary>
    /// Unsubscribes from the Service Bus queue or topic and stops processing messages.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override ValueTask Unsubscribe(CancellationToken cancellationToken)
    {
        return new ValueTask(processor?.StopProcessingAsync(cancellationToken) ?? Task.CompletedTask);
    }
}
