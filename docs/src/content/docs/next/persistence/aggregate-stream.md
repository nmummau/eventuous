---
title: "Event streams"
description: "How state maps to event streams"
sidebar:
  order: 1
---

## Concept

In Event Sourcing, each entity instance has its own stream of events. The stream name uniquely identifies the entity and is used by the [event store](../event-store) to read and write events.

When appending events to a stream, the append operation for a single stream must be transactional to ensure consistency. Eventuous handles commands using the [command service](../../application/app-service), and one command handler is the unit of work. All the events produced during the unit of work are appended to the stream as the final step in the command handling process.

This applies equally to both the aggregate-based and the [functional](../../application/func-service) command service — both ultimately store events against a stream derived from the entity's state type and identity.

## Stream name

By default, Eventuous derives the stream name from the **type name** and the **entity id**. For example, `BookingState` with id `1` produces a stream name `Booking-1`.

There are several ways to construct a `StreamName`:

```csharp
// Using any type — uses the type name as the prefix
StreamName.For<Booking>("123");       // "Booking-123"

// Using a State type — strips the "State" suffix automatically
StreamName.ForState<BookingState>("123"); // "Booking-123"
```

`StreamName.For<T>` is unconstrained — `T` can be any type, not just an aggregate. However, `StreamName.ForState<TState>` is specifically designed for state types and strips the `State` suffix to produce a clean stream name.

## Custom stream names

You might want more fine-grained control over the stream name — for example, to include a tenant id. You can override the default convention by configuring a `StreamNameMap`. The map registers a function per **identity type** (derived from `Id`), so any property on your identity record can be used to produce the stream name.

For example, given a custom identity with a tenant:

```csharp title="BookingId.cs"
public record BookingId : Id {
    public BookingId(string id, string tenantId) : base(id) {
        TenantId = tenantId;
    }

    public string TenantId { get; }
}
```

Register the mapping and add it to the container:

```csharp title="Program.cs"
var streamNameMap = new StreamNameMap();
streamNameMap.Register<BookingId>(
    id => new StreamName($"Booking-{id.TenantId}:{id.Value}")
);
builder.Services.AddSingleton(streamNameMap);
builder.Services.AddCommandService<BookingService, Booking>();
```

Then pass the `StreamNameMap` to your command service:

```csharp title="BookingService.cs"
public class BookingService : CommandService<Booking, BookingState, BookingId> {
    public BookingService(IEventStore store, StreamNameMap streamNameMap)
        : base(store, streamNameMap: streamNameMap) {
        // command handlers registered here
    }
}
```

:::note
`StreamNameMap.Register<TId>` is keyed by the **Id type**, not by the aggregate or state type. One registration per identity type applies wherever that identity is used.
:::

## Extracting identity from stream names

In projections, you can extract the id from the stream name available in the consume context. For multi-tenant stream names with a separator, you can write a simple extension:

```csharp title="StreamNameExtensions.cs"
public static class StreamNameExtensions {
    public static (string TenantId, string Id) ExtractMultiTenantIds(
        this StreamName stream, char separator = ':'
    ) {
        var streamId = stream.GetId();
        var parts = streamId.Split(separator);

        if (parts.Length != 2)
            throw new InvalidStreamName(streamId);

        return (parts[0], parts[1]);
    }
}
```

Then use it in a projection handler:

```csharp title="BookingStateProjection.cs"
static UpdateDefinition<BookingDocument> HandleRoomBooked(
    IMessageConsumeContext<V1.RoomBooked> ctx,
    UpdateDefinitionBuilder<BookingDocument> update
) {
    var evt = ctx.Message;
    var (tenantId, id) = ctx.Stream.ExtractMultiTenantIds();

    return update
        .SetOnInsert(x => x.Id, id)
        .SetOnInsert(x => x.TenantId, tenantId)
        .Set(x => x.GuestId, evt.GuestId)
        .Set(x => x.RoomId, evt.RoomId)
        .Set(x => x.CheckInDate, evt.CheckInDate)
        .Set(x => x.CheckOutDate, evt.CheckOutDate)
        .Set(x => x.BookingPrice, evt.BookingPrice)
        .Set(x => x.Outstanding, evt.OutstandingAmount);
}
```
