using Eventuous.Producers;
using Eventuous.Producers.Diagnostics;
using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.AMQP;
using RabbitMQ.Stream.Client.Reliable;

namespace Eventuous.RabbitMq.Producers;

public class RabbitMqEventStoreProducer : BaseProducer<RabbitMqProduceOptions> {
    readonly Producer _producer;
    readonly IEventSerializer _serializer;
    readonly IMetadataSerializer _metaSerializer;

    public RabbitMqEventStoreProducer(
        Producer producer,
        IEventSerializer? serializer = null,
        IMetadataSerializer? metaSerializer = null
    ) : base(new ProducerTracingOptions {
        MessagingSystem = "rabbitmq-streams",
        DestinationKind = "stream",
        ProduceOperation = "append"
    }) {
        _producer = producer;
        _serializer = serializer ?? DefaultEventSerializer.Instance;
        _metaSerializer = metaSerializer ?? DefaultMetadataSerializer.Instance;
    }

    protected override async Task ProduceMessages(
        StreamName stream,
        IEnumerable<ProducedMessage> messages,
        RabbitMqProduceOptions? options,
        CancellationToken cancellationToken = default
    ) {
        var batch = messages.ToList();
        foreach (var msg in batch) {
            var (eventType, contentType, payload) = _serializer.SerializeEvent(msg.Message);
            var metaBytes = _metaSerializer.Serialize(msg.Metadata ?? new Metadata());

            var properties = new Properties {
                MessageId = msg.MessageId.ToString(),
                ContentType = contentType
            };

            var appProps = new ApplicationProperties();
            foreach (var kv in msg.Metadata ?? new Metadata()) {
                if (kv.Value is not null)
                    appProps[kv.Key] = kv.Value.ToString();
            }
            appProps.Add("EventType", eventType);

            var body = CombinePayloadAndMetadata(payload, metaBytes);

            var message = new Message(body) {
                Properties = properties,
                ApplicationProperties = appProps
            };

            await _producer.Send(message);
            await msg.Ack<RabbitMqEventStoreProducer>();
        }
    }

    private static byte[] CombinePayloadAndMetadata(byte[] payload, byte[] metadata) {
        var result = new byte[payload.Length + metadata.Length];
        Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
        Buffer.BlockCopy(metadata, 0, result, payload.Length, metadata.Length);
        return result;
    }
}