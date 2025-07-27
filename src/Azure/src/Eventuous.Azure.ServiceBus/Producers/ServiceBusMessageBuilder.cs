using Eventuous.Azure.ServiceBus.Shared;
using Eventuous.Producers;

namespace Eventuous.Azure.ServiceBus.Producers;

internal class ServiceBusMessageBuilder
{
    private static readonly HashSet<string> ReservedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            MetaTags.MessageId,
            MetaTags.CorrelationId,
        };

    private readonly IEventSerializer serializer;
    private readonly string streamName;
    private readonly ServiceBusProduceOptions? options;
    private readonly ServiceBusMessageAttributes attributes;
    private readonly Action<string>? setActivityMessageType;

    internal ServiceBusMessageBuilder(IEventSerializer serializer, string streamName, ServiceBusMessageAttributes attributes, ServiceBusProduceOptions? options = null, Action<string>? setActivityMessageType = null)
    {
        this.serializer = serializer;
        this.streamName = streamName;
        this.options = options;
        this.attributes = attributes;
        this.setActivityMessageType = setActivityMessageType;
    }

    /// <summary>
    /// Creates a <see cref="ServiceBusMessage"/> from the provided <see cref="ProducedMessage"/>.
    /// This method serializes the event, sets the necessary properties, and adds custom application properties
    /// based on the metadata and additional headers.
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    internal ServiceBusMessage CreateServiceBusMessage(ProducedMessage message)
    {
        var (messageType, contentType, payload) = serializer.SerializeEvent(message.Message);
        setActivityMessageType?.Invoke(messageType);

        var serviceBusMessage = new ServiceBusMessage(payload)
        {
            ContentType = contentType,
            MessageId = message.MessageId.ToString(),
            Subject = options?.Subject,
            TimeToLive = options?.TimeToLive ?? TimeSpan.MaxValue,
            CorrelationId = message.Metadata?.GetCorrelationId(),
            To = options?.To,
            ReplyTo = options?.ReplyTo
        };

        foreach (var property in GetCustomApplicationProperties(message, messageType))
        {
            serviceBusMessage.ApplicationProperties.Add(property);
        }

        return serviceBusMessage;
    }

    private IEnumerable<KeyValuePair<string, object>> GetCustomApplicationProperties(ProducedMessage message, string messageType) =>
     (message.Metadata ?? [])
        .Concat(message.AdditionalHeaders ?? [])
        .Concat(
        [
            new (attributes.EventType, messageType),
            new (attributes.StreamName, streamName),
        ])
        .Where(pair => !ReservedAttributes.Contains(pair.Key))
        .Where(pair => pair.Value is not null)
        .Select(pair => new KeyValuePair<string, object>(pair.Key, pair.Value!));
}
