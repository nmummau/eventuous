---
title: "Event store"
description: "Event store infrastructure"
sidebar:
  order: 5
---

To isolate the core library from a particular way of storing events, Eventuous uses the `IEventStore` abstraction. Whilst it's used by `AggregateStore`, you can also use it in a more generic way, when you need to persist or read events without having an aggregate.

The `IEventStore` interface inherits from `IEventReader` and `IEventWriter` interfaces. Each of those interfaces is focused on one specific task – reading events from streams and appending events to streams. This separation is necessary for scenarios when you only need, for example, to read events from a specific store but not to append them. In such a case, you'd want to use the `IEventReader` interface only.

Eventuous has several implementations of event store abstraction, you can find them in the [infrastructure](../../infra/esdb) section. The default implementation is `KurrentDBEventStore`, which uses [KurrentDB](https://kurrent.io) as the event store. It's a great product, and we're happy to provide first-class support for it. It's also a great product for learning about Event Sourcing and CQRS.

In addition, Eventuous has an in-memory event store, which is mostly used for testing purposes. It's not recommended to use it in production, as it doesn't provide any persistence guarantees.

### Primitives

Event store works with a couple of primitives, which allow wrapping infrastructure-specific structures. Those primitives are:

| Record type             | What it's for                                                                                                                                         |
|-------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------|
| `StreamReadPosition`    | Represent the stream revision, from there the event store will read the stream forwards or backwards.                                                 |
| `ExpectedStreamVersion` | The stream version (revision), which we expect to have in the database, when event store tries to append new events. Used for optimistic concurrency. |
| `StreamEvent`           | A structure, which holds the event type as a string as well as serialised event payload and metadata.                                                 |

All of those are immutable records.

### Operations

The event store provides the following operations:

| Function         | What's it for                                                    |
|------------------|------------------------------------------------------------------|
| `AppendEvents`   | Append one or more events to a given stream.                     |
| `AppendEvents`   | Append events to multiple streams in a single operation.         |
| `ReadEvents`     | Read events from a stream forwards, from a given start position. |
| `StreamExists`   | Check if a stream exists.                                        |
| `TruncateStream` | Remove events from a stream up to a given position.              |
| `DeleteStream`   | Delete a stream entirely.                                        |

## Usage examples

### Appending events to a stream

Use `AppendEvents` to persist events. The `ExpectedStreamVersion` parameter provides optimistic concurrency control.

```csharp
var streamName = new StreamName("Order-123");

var events = new[] {
    new NewStreamEvent(Guid.NewGuid(), new OrderCreated("123", 99.99m), new Metadata()),
    new NewStreamEvent(Guid.NewGuid(), new OrderConfirmed("123"), new Metadata())
};

// Append to a new stream
var result = await eventStore.AppendEvents(
    streamName,
    ExpectedStreamVersion.NoStream, // stream must not exist yet
    events,
    cancellationToken
);

// result.NextExpectedVersion can be used for subsequent appends
```

The `Store` extension method accepts plain domain event objects and handles wrapping them into `NewStreamEvent` instances:

```csharp
var streamName = new StreamName("Order-123");
object[] changes = [new OrderCreated("123", 99.99m), new OrderConfirmed("123")];

var result = await eventStore.Store(
    streamName,
    ExpectedStreamVersion.NoStream,
    changes,
    cancellationToken: cancellationToken
);
```

### Multi-stream append

You can append events to multiple streams in a single operation using the multi-stream overload of `AppendEvents`:

```csharp
NewStreamAppend[] appends = [
    new(orderStream, ExpectedStreamVersion.NoStream, orderEvents),
    new(inventoryStream, new ExpectedStreamVersion(currentVersion), inventoryEvents)
];

AppendEventsResult[] results = await eventStore.AppendEvents(appends, cancellationToken);
```

Each element specifies a target stream, its expected version, and the events to append. The return array contains one `AppendEventsResult` per stream in the same order as the input.

**Atomicity guarantees vary by store:**

| Store                  | Atomicity                                                           |
|------------------------|---------------------------------------------------------------------|
| KurrentDB (25.1+)      | Atomic — all streams updated or entire operation fails              |
| PostgreSQL             | Atomic — uses a single database transaction                         |
| SQL Server             | Atomic — uses a single database transaction                         |
| SQLite                 | Atomic — uses a single database transaction                         |
| Default (other stores) | Not atomic — streams are written sequentially, fails on first error |

### Reading events from a stream

```csharp
var streamName = new StreamName("Order-123");

// Read up to 100 events from the start
var events = await eventStore.ReadEvents(
    streamName,
    StreamReadPosition.Start,
    count: 100,
    failIfNotFound: true,
    cancellationToken
);

foreach (var evt in events) {
    // evt.Payload contains the deserialized event object
    // evt.Revision is the position within the stream
    // evt.Created is the timestamp
    Console.WriteLine($"Event {evt.Revision}: {evt.Payload}");
}
```

The `ReadStream` extension method handles pagination automatically and returns all events:

```csharp
var allEvents = await eventStore.ReadStream(
    streamName,
    StreamReadPosition.Start,
    failIfNotFound: true,
    cancellationToken
);
```

### Stream management

```csharp
// Check if a stream exists
bool exists = await eventStore.StreamExists(streamName, cancellationToken);

// Truncate a stream up to a given position
await eventStore.TruncateStream(
    streamName,
    new StreamTruncatePosition(5),
    new ExpectedStreamVersion(currentVersion),
    cancellationToken
);

// Delete a stream
await eventStore.DeleteStream(
    streamName,
    new ExpectedStreamVersion(currentVersion),
    cancellationToken
);
```

### Loading and storing aggregates

Extension methods on `IEventReader` and `IEventWriter` provide aggregate-level operations:

```csharp
// Load an aggregate from its event stream
var booking = await eventReader.LoadAggregate<Booking, BookingState, BookingId>(
    bookingId,
    cancellationToken: cancellationToken
);

// Execute domain logic
booking.Confirm();

// Persist new events
await eventWriter.StoreAggregate<Booking, BookingState, BookingId>(
    booking,
    bookingId,
    cancellationToken: cancellationToken
);
```

## Supported stores

Eventuous has several implementations of the event store:
 * [KurrentDB](../../infra/esdb)
 * [PostgreSQL](../../infra/postgres)
 * [Microsoft SQL Server](../../infra/mssql)
 * [SQLite](../../infra/sqlite)
 * [Elasticsearch](../../infra/elastic)

If you use one of the implementations provided, you won't need to know about the event store abstraction. It is required though if you want to implement it for your preferred database.

:::tip
Preferring KurrentDB will save you lots of time!
Remember to check [Kurrent Cloud](https://kurrent.io/cloud).
:::
