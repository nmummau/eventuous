# Multi-Stream Produce Design

## Goal

Add a multi-stream `Produce` overload to `IProducer` and `IProducer<TProduceOptions>` for batch efficiency and API simplification. Update Gateway to use the new overload.

## New Types

Located in `src/Core/src/Eventuous.Producers/`.

```csharp
// Non-generic, for IProducer
[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest(StreamName Stream, IEnumerable<ProducedMessage> Messages);

// Generic, for IProducer<TProduceOptions>
[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest<TProduceOptions>(
    StreamName Stream,
    IEnumerable<ProducedMessage> Messages,
    TProduceOptions? Options
) where TProduceOptions : class;
```

## Interface Changes

### IProducer

New default interface method with parallel execution:

```csharp
Task Produce(IReadOnlyCollection<ProduceRequest> requests, CancellationToken ct = default) {
    return Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, ct)));
}
```

### IProducer<TProduceOptions>

New default interface method with parallel execution:

```csharp
Task Produce(IReadOnlyCollection<ProduceRequest<TProduceOptions>> requests, CancellationToken ct = default) {
    return Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, r.Options, ct)));
}
```

## BaseProducer Changes

Override multi-stream `Produce` to add tracing at batch level, then delegate to a new virtual `ProduceMessages` overload. Implementations can override for optimized multi-stream behavior; default calls single-stream `ProduceMessages` in parallel.

## Gateway Update

`GatewayHandler` replaces `GroupBy` + parallel per-stream `Produce` with a single call to multi-stream `Produce`, constructing `ProduceRequest<TProduceOptions>` from the transformed messages.

## What Doesn't Change

- Individual producer implementations (Kafka, RabbitMQ, PubSub, Service Bus) inherit the default parallel behavior. They can override later for optimization.
- Existing single-stream `Produce` signatures remain untouched.
- Ack/nack semantics unchanged — `ProducedMessage` already carries callbacks.

## Testing

- Unit tests for default parallel behavior
- Gateway integration test verifying multi-stream produce works end-to-end
