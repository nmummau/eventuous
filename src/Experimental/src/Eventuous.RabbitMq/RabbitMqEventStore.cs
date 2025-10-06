using Microsoft.Extensions.Logging;
using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.AMQP;
using RabbitMQ.Stream.Client.Reliable;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mime;
using System.Runtime.Serialization;
using System.Text;

namespace Eventuous.RabbitMq;

public class RabbitMqEventStore : IEventStore {
    readonly IEventSerializer    _serializer;
    readonly IMetadataSerializer _metaSerializer;
    readonly IRabbitMqStream     _rabbitMqStream;
    readonly StreamSystem        _streamSystem;
    readonly ConcurrentDictionary<string, Producer> _producers = new();

    public RabbitMqEventStore(
        IRabbitMqStream          rabbitMqStream,
        IEventSerializer?        serializer     = null,
        IMetadataSerializer?     metaSerializer = null
    ) {
        _rabbitMqStream = rabbitMqStream ?? throw new ArgumentNullException(nameof(rabbitMqStream));
        _serializer     = serializer     ?? DefaultEventSerializer.Instance;
        _metaSerializer = metaSerializer ?? DefaultMetadataSerializer.Instance;
        _streamSystem   = _rabbitMqStream.CreateStreamSystem().GetAwaiter().GetResult();
    }

    public async Task<AppendEventsResult> AppendEvents(
        StreamName                          stream,
        ExpectedStreamVersion               expectedVersion,
        IReadOnlyCollection<NewStreamEvent> events,
        CancellationToken                   cancellationToken = default
    ) {
        if (events.Count == 0) return AppendEventsResult.NoOp;

        if (!await _streamSystem.StreamExists(stream)) {
            if (expectedVersion == ExpectedStreamVersion.NoStream) {
                await _streamSystem.CreateStream(new(stream));
            } else {
                throw new InvalidOperationException($"Stream {stream} does not exist");
            }
        }

        var producer = _producers.GetOrAdd(
            stream,
            name => Producer.Create(new(_streamSystem, name)).GetAwaiter().GetResult()
        );

        // OPTIONAL read tail & compute lastVersion if you want OCC by version header
        var lastVersion = -1; // TODO: tail-read and read ApplicationProperties["AggregateVersion"]

        // Use TryQueryOffset for concurrency check if needed
        if (expectedVersion != ExpectedStreamVersion.Any &&
            expectedVersion != ExpectedStreamVersion.NoStream) {
            var lastOffset = await _rabbitMqStream.TryQueryOffset(stream, "eventuous-version");
            if (lastOffset != (ulong)expectedVersion.Value) {
                throw new AppendToStreamException(stream, new($"Expected version {expectedVersion.Value} but found {lastOffset}"));
            }

            if (lastVersion != expectedVersion.Value) {
                // TODO
                var error = $"Wrong expected version. Expected {expectedVersion.Value}, got {lastVersion}";
                //throw new OptimisticConcurrencyException(stream, new InvalidOperationException(
                //    $"Wrong expected version. Expected {expectedVersion.Value}, got {lastVersion}"));
            }
        }

        const ulong lastPublishedOffset = 0;
        var nextVersion = lastVersion;
        foreach (var evt in events) {
            nextVersion++;

            var (eventType, contentType, payload) = _serializer.SerializeEvent(evt.Payload!);

            // Set AMQP Properties
            var properties = new Properties {
                MessageId   = evt.Id.ToString(),
                ContentType = "application/json"
            };

            // headers: put metadata here (strings), plus our reserved keys
            var appProps = new ApplicationProperties {
                ["EventType"]        = eventType,
                ["AggregateStream"]  = (string)stream,
                ["AggregateVersion"] = nextVersion
            };

            foreach (var kv in evt.Metadata) {
                if (kv.Value is not null) {
                    appProps[kv.Key] = kv.Value.ToString();
                }
            }

            // Construct the Message
            var message = new Message(payload) {
                Properties            = properties,
                ApplicationProperties = appProps
            };

            var json = Encoding.UTF8.GetString(payload);
            Console.WriteLine($"APPEND {eventType}: {json}");

            await producer.Send(message);
        }

        // Store the new offset for concurrency tracking
        // await _rabbitMqStream.StoreOffset(stream, "eventuous-version", lastPublishedOffset);

        return new AppendEventsResult(GlobalPosition: 0, NextExpectedVersion: nextVersion);
    }

    // ... other IEventStore methods ...
    public async Task<StreamEvent[]> ReadEvents(
            StreamName         stream,
            StreamReadPosition start,
            int                count,
            bool               failIfNotFound,
            CancellationToken  cancellationToken
        ) {
        var physical = (string)stream;
        if (!await _streamSystem.StreamExists(physical).ConfigureAwait(false)) {
            if (failIfNotFound) throw new StreamNotFound(stream);
            return [];
        }

        var results = new List<StreamEvent>();
        var done    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var jsonString = "";

        var consumer = await Consumer.Create(new(_streamSystem, physical) {
            OffsetSpec         = new OffsetTypeFirst(),
            Reference          = $"probe-{Guid.NewGuid():N}",
            ClientProvidedName = "probe-reader",
            MessageHandler = async (_, raw, ctx, msg) => {
                //var body = new byte[msg.Data.Size];
                var body = msg.Data.Contents;
                //msg.Data.Write(body);

                var s = System.Text.Encoding.UTF8.GetString(body);
                Console.WriteLine($"READ RAW: {s}  (startsWith '{{' = {s.Length > 0 && s[0] == '{'})");

                // just wrap raw JSON as-is
                var json = Encoding.UTF8.GetString(body);
                jsonString = json;
                var id   = Guid.TryParse((string)msg.Properties?.MessageId, out var gid) ? gid : Guid.NewGuid();

                results.Add(new StreamEvent(id, json, new Metadata(), "application/json", (long)ctx.Offset));

                await raw.Close();
                done.TrySetResult(true);
            }
        });

        await Task.WhenAny(done.Task, Task.Delay(2000, cancellationToken));
        await consumer.Close();

        return results.ToArray();
    }

    StreamEvent ToStreamEvent(MessageContext ctx, Message msg, string stream) {
        // 1) Body
        var body = new byte[msg.Data.Size];
        msg.Data.Write(body);

        // 2) Headers
        var eventType = msg.ApplicationProperties?.TryGetValue("EventType", out var et) == true
            ? et?.ToString() ?? ""
            : "";

        var contentType = msg.Properties?.ContentType ?? "application/json";

        // 3) Deserialize payload
        var dr = _serializer.DeserializeEvent(body, eventType, contentType);

        // 4) Metadata (from ApplicationProperties, exclude reserved keys)
        var meta = new Metadata();
        if (msg.ApplicationProperties is { } app) {
            foreach (var kv in app) {
                var k = kv.Key;
                if (k is "EventType" or "AggregateStream" or "AggregateVersion") continue;
                meta[k] = kv.Value?.ToString();
            }
        }

        // 5) MessageId & Position
        var idStr = msg.Properties?.MessageId?.ToString();
        var id    = Guid.TryParse(idStr, out var gid) ? gid : Guid.NewGuid();
        var pos   = (long)ctx.Offset;

        // 6) Mirror SqlEventStore behavior
        return dr switch {
            DeserializationResult.SuccessfullyDeserialized ok => new StreamEvent(id, ok.Payload, meta, contentType, pos),
            DeserializationResult.FailedToDeserialize fail => throw new SerializationException(
                $"Can't deserialize {eventType} from stream {stream}: {fail.Error}"
            ),
            _ => throw new SerializationException("Unknown deserialization result")
        };
    }

    public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, bool failIfNotFound, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task TruncateStream(StreamName stream, StreamTruncatePosition truncatePosition, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public interface IRabbitMqConfiguration {
    public string Host        { get; }
    public string Username    { get; }
    public string Password    { get; }
    public string VirtualHost { get; }
    public int    StreamPort  { get; }
}

public class RabbitMqConfiguration : IRabbitMqConfiguration {
    private const int DefaultStreamPort = 5552;

    public string Host        { get; set; } = string.Empty;
    public string Username    { get; set; } = string.Empty;
    public string Password    { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = string.Empty;
    public int    StreamPort  { get; set; } = DefaultStreamPort;
}

public interface IRabbitMqStream {
    Task<StreamSystem> CreateStreamSystem();
    Task<ulong?> TryQueryOffset(string stream, string reference);
    Task StoreOffset(string            stream, string reference, ulong offsetValue);
}

public class RabbitMqStream(ILogger<StreamSystem> streamSystemLogger, IRabbitMqConfiguration rabbitMqConfig) : IRabbitMqStream {
    public Task<StreamSystem> CreateStreamSystem() {
        var config = new StreamSystemConfig {
            UserName        = rabbitMqConfig.Username,
            Password        = rabbitMqConfig.Password,
            VirtualHost     = rabbitMqConfig.VirtualHost,
            Endpoints       = [new DnsEndPoint(rabbitMqConfig.Host, rabbitMqConfig.StreamPort)],
            AddressResolver = new(new DnsEndPoint(rabbitMqConfig.Host, rabbitMqConfig.StreamPort))
        };

        return StreamSystem.Create(config, streamSystemLogger);
    }

    public async Task<ulong?> TryQueryOffset(string stream, string reference) {
        var streamSystem = await CreateStreamSystem()
            .ConfigureAwait(false);

        var offset = await streamSystem.TryQueryOffset(reference, stream)
            .ConfigureAwait(false);

        await streamSystem.Close()
            .ConfigureAwait(false);

        return offset;
    }

    public async Task StoreOffset(string stream, string reference, ulong offsetValue) {
        var streamSystem = await CreateStreamSystem()
            .ConfigureAwait(false);

        await streamSystem.StoreOffset(reference, stream, offsetValue)
            .ConfigureAwait(false);

        await streamSystem.Close()
            .ConfigureAwait(false);
    }
}
