---
title: "SignalR"
description: "Real-time event streaming to clients via SignalR"
sidebar:
  order: 11
---

## Introduction

The SignalR subscription gateway bridges Eventuous stream subscriptions to SignalR, enabling real-time event streaming to browser UIs, mobile apps, or other remote clients. It provides two NuGet packages:

- **`Eventuous.SignalR.Server`** — server-side gateway that manages per-connection Eventuous subscriptions and forwards events over SignalR
- **`Eventuous.SignalR.Client`** — client-side subscription API with auto-reconnect and typed event handling

The server reuses the existing [Gateway](../../../gateway) pattern (`GatewayHandler` + `BaseProducer`) internally, so event forwarding benefits from the same tracing and metadata pipeline as other Eventuous producers.

## Server

### Registration

Register the gateway with a subscription factory that creates store-specific subscriptions on demand:

```csharp
builder.Services.AddSignalRSubscriptionGateway<SignalRSubscriptionHub>((sp, options) => {
    var client = sp.GetRequiredService<KurrentDBClient>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    options.SubscriptionFactory = (stream, fromPosition, pipe, subscriptionId) =>
        new StreamSubscription(client, new StreamSubscriptionOptions {
            StreamName = stream,
            SubscriptionId = subscriptionId
        }, new NoOpCheckpointStore(fromPosition), pipe, loggerFactory);
});
```

The `SubscriptionFactory` delegate is called each time a client subscribes to a stream. It receives the stream name, optional starting position, a pre-built consume pipe, and a subscription identifier. You can use any Eventuous subscription type (KurrentDB, PostgreSQL, etc.).

### Hub

Map the ready-made hub to an endpoint:

```csharp
app.MapHub<SignalRSubscriptionHub>("/subscriptions");
```

The built-in `SignalRSubscriptionHub` exposes two methods that clients call:

- `SubscribeToStream(string stream, ulong? fromPosition)` — start receiving events
- `UnsubscribeFromStream(string stream)` — stop receiving events

When a client disconnects, all its subscriptions are automatically cleaned up.

### Custom hubs

For applications that need a custom hub (e.g., adding authentication or authorization logic), inject `SubscriptionGateway<THub>` directly:

```csharp
public class MyHub(SubscriptionGateway<MyHub> gateway) : Hub {
    public Task SubscribeToStream(string stream, ulong? fromPosition)
        => gateway.SubscribeAsync(Context.ConnectionId, stream, fromPosition, Context.ConnectionAborted);

    public Task UnsubscribeFromStream(string stream)
        => gateway.UnsubscribeAsync(Context.ConnectionId, stream);

    public override Task OnDisconnectedAsync(Exception? exception)
        => gateway.RemoveConnectionAsync(Context.ConnectionId);
}
```

Register with your custom hub type:

```csharp
builder.Services.AddSignalRSubscriptionGateway<MyHub>((sp, options) => {
    // configure subscription factory
});
```

## Client

### Connection setup

Create a `SignalRSubscriptionClient` from any `HubConnection`. The client hooks into SignalR's reconnect lifecycle but doesn't own the connection policy — configure automatic reconnect on the `HubConnection` itself:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://myserver/subscriptions")
    .WithAutomaticReconnect()
    .Build();

await connection.StartAsync();

var client = new SignalRSubscriptionClient(connection);
```

### Raw streaming with IAsyncEnumerable

The simplest consumption mode returns events as they arrive:

```csharp
await foreach (var envelope in client.SubscribeAsync("Order-123", fromPosition: null)) {
    Console.WriteLine($"{envelope.EventType} at position {envelope.StreamPosition}");
    Console.WriteLine(envelope.JsonPayload);
}
```

Each `StreamEventEnvelope` contains:

| Property | Description |
|---|---|
| `EventId` | Unique event identifier |
| `Stream` | Source stream name |
| `EventType` | Registered event type name |
| `StreamPosition` | Position within the stream |
| `GlobalPosition` | Position in the global event log |
| `Timestamp` | When the event was created |
| `JsonPayload` | Event payload as JSON |
| `JsonMetadata` | Event metadata as JSON (may include trace context) |

### Typed consumption with On&lt;T&gt;

For type-safe event handling, use `SubscribeTyped` with fluent handler registration:

```csharp
await client.SubscribeTyped("Order-123", fromPosition: 0)
    .On<OrderPlaced>((evt, meta) => {
        Console.WriteLine($"Order placed: {evt.OrderId} at {meta.Timestamp}");
        return ValueTask.CompletedTask;
    })
    .On<OrderShipped>((evt, meta) => {
        Console.WriteLine($"Order shipped at position {meta.Position}");
        return ValueTask.CompletedTask;
    })
    .OnError(err => Console.WriteLine($"Error on {err.Stream}: {err.Message}"))
    .StartAsync();
```

Events are deserialized using the Eventuous `TypeMap` and `IEventSerializer`. Event types must be registered in `TypeMap` as usual (via `[EventType]` attribute or manual registration). Unrecognized event types are silently skipped.

All `On<T>` handlers must be registered before calling `StartAsync`. Calling `On<T>` after `StartAsync` throws `InvalidOperationException`.

### Client options

```csharp
var client = new SignalRSubscriptionClient(connection, new SignalRSubscriptionClientOptions {
    Serializer = customSerializer,  // default: DefaultEventSerializer.Instance
    EnableTracing = true            // default: false
});
```

| Option | Description |
|---|---|
| `Serializer` | Custom `IEventSerializer` for deserializing event payloads in typed mode |
| `EnableTracing` | When `true`, the client creates an `Activity` for each received event, linked to the trace context from metadata. Enable when the client has an OpenTelemetry collector configured. |

## Auto-reconnect

The client handles connection drops transparently:

1. **Position tracking** — the client records the last stream position for each active subscription
2. **Re-subscribe on reconnect** — when SignalR reconnects, the client re-sends `SubscribeToStream` for each active subscription with the last known position
3. **Deduplication** — events at or before the last seen position are skipped, preventing duplicates after reconnect

The server is stateless — it creates fresh subscriptions from the positions provided by the client.

```
Normal flow:
  Client ──SubscribeToStream("Order-1", 42)──► Server
  Client ◄──StreamEvent(pos=43)──────────────── Server
  Client ◄──StreamEvent(pos=44)──────────────── Server
         [tracks lastPosition = 44]

Disconnect + Reconnect:
  [connection drops, SignalR reconnects]
  Client ──SubscribeToStream("Order-1", 44)──► Server
  Client ◄──StreamEvent(pos=44)──────────────── Server  [duplicate, skipped]
  Client ◄──StreamEvent(pos=45)──────────────── Server  [new, delivered]
```

## Wire format

Events are transmitted as `StreamEventEnvelope` records over SignalR. The payload is pre-serialized JSON on the server side, avoiding polymorphic serialization issues. Metadata (including trace context) flows through `JsonMetadata` as a serialized dictionary.

Trace context propagates through the existing Eventuous metadata pipeline: `$traceId` and `$spanId` keys in metadata are preserved from the original event through the gateway to the client. When `EnableTracing` is enabled on the client, the consume activity is linked to the original trace.

## Packages

| Package | Dependencies | Purpose |
|---|---|---|
| `Eventuous.SignalR.Server` | `Eventuous.Subscriptions`, `Eventuous.Gateway`, `Microsoft.AspNetCore.App` | Server-side gateway |
| `Eventuous.SignalR.Client` | `Eventuous.Shared`, `Eventuous.Serialization`, `Eventuous.Diagnostics`, `Microsoft.AspNetCore.SignalR.Client` | Client-side subscriptions |
