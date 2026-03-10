---
title: "Serialization"
description: "How events are serialized and deserialized"
sidebar:
  order: 40
---

As described on the [Domain events](../../domain/domain-events) page, events must be (de)serializable. Eventuous doesn't care about the serialization format, but requires you to provide a serializer instance, which implements the `IEventSerializer` interface.

The serializer interface is simple:

```csharp title="IEventSerializer.cs"
public interface IEventSerializer {
    DeserializationResult DeserializeEvent(ReadOnlySpan<byte> data, string eventType, string contentType);

    SerializationResult SerializeEvent(object evt);
}
```

The serialization result contains not only the serialized object as bytes, but also the event type as string (see below), and the content type:

```csharp
public record SerializationResult(string EventType, string ContentType, byte[] Payload);
```

### Type map

For deserialization, the serializer will get the binary payload and the event type as string. Event store is unaware of your event types, it just stores the payload in a binary format to the database, along with the event type as string. It is up to you how your strong event types map to the event type string.

:::caution
We do not advise using fully-qualified type names as event types. It will block your ability to refactor the domain model code.
:::

Therefore, we need to have a way to map strong types of the events to strings, which are used to identify those types in the database and for serialization. For that purpose, Eventuous uses the `TypeMap`. It is a singleton, which is available globally. When you add new events to your domain model, remember to also add a mapping for those events. The mapping is static, so you can implement it anywhere in the application. The only requirement is that the mapping code must execute when the application starts.

For example, if you have a place where domain events are defined, you can put the mapping code there, as a static member:

```csharp title="BookingEvents.cs"
static class BookingEvents {
    // events are defined here

    public static void MapBookingEvents() {
        TypeMap.AddType<RoomBooked>("RoomBooked");
        TypeMap.AddType<BookingPaid>("BookingPaid");
        TypeMap.AddType<BookingCancelled>("BookingCancelled");
        TypeMap.AddType<BookingImported>("BookingImported");
    }
}
```

Then, you can call this code in your bootstrap code:

```csharp title="Program.cs"
BookingEvents.MapBookingEvents();
```

### Auto-registration with source generator

The recommended way to register event types is to use the `[EventType]` attribute combined with the Eventuous source generator. The generator automatically discovers all types decorated with `[EventType]` in your project and generates a module initializer that registers them at startup — no manual registration code needed.

Annotate your events with the `[EventType]` attribute:

```csharp
[EventType("V1.FullyPaid")]
public record BookingFullyPaid(string BookingId, DateTimeOffset FullyPaidAt);

[EventType("V1.RoomBooked")]
public record RoomBooked(string RoomId, LocalDate CheckIn, LocalDate CheckOut, float Price);
```

That's it. The source generator produces a module initializer class per assembly, which calls `TypeMap.Instance.AddType(...)` for each annotated event type. Registration happens automatically when the assembly is loaded — you don't need to write any startup code.

:::tip
Eventuous also includes a diagnostic analyzer (`EVTC001`) that warns you when an event type is used in aggregates or state projections but is missing the `[EventType]` attribute.
:::

### Reflection-based registration

As an alternative to the source generator, you can use reflection-based registration. This scans assemblies at runtime for types decorated with `[EventType]`:

```csharp
TypeMap.RegisterKnownEventTypes();
```

The registration won't work if event classes are defined in another assembly, which hasn't been loaded yet. You can work around this limitation by specifying one or more assemblies explicitly:

```csharp
TypeMap.RegisterKnownEventTypes(typeof(BookingFullyPaid).Assembly);
```

:::note
With the source generator in place, calling `RegisterKnownEventTypes()` is typically unnecessary. The generator handles registration at compile time, which is both more reliable and avoids the overhead of runtime assembly scanning.
:::

### Default serializer

Eventuous provides a default serializer implementation, which uses `System.Text.Json`. You just need to register it in the `Startup` to make it available for the infrastructure components, like [aggregate store](../aggregate-store) and [subscriptions](../../subscriptions/subs-concept).

Normally, you don't need to register or provide the serializer instance to any of the Eventuous classes that perform serialization and deserialization work. It's because they will use the default serializer instance instead.

However, you can register the default serializer with different options, or a custom serializer instead:

```csharp title="Program.cs"
builder.Services.AddSingleton<IEventSerializer>(
    new DefaultEventSerializer(
        new JsonSerializerOptions(JsonSerializerDefaults.Default)
    )
);
```

You might want to avoid registering the serializer and override the one that Eventuous uses as the default instance:

```csharp title="Program.cs"
var defaultSerializer = new DefaultEventSerializer(
    new JsonSerializerOptions(JsonSerializerDefaults.Default)
);
DefaultEventSerializer.SetDefaultSerializer(serializer);
```

### Metadata serializer

In many cases you might want to store event metadata in addition to the event payload. Normally, you'd use the same way to serialize both the event payload and its metadata, but it's not always the case. For example, you might store your events in Protobuf, but keep metadata as JSON.

Eventuous only uses the metadata serializer when the event store implementation, or a producer can store metadata as a byte array. For example, KurrentDB supports that, but Google PubSub doesn't. Therefore, the event store and producer that use KurrentDB will use the metadata serializer, but the Google PubSub producer will add metadata to events as headers, and won't use the metadata serializer.

For the metadata serializer the same principles apply as for the event serializer. Eventuous has a separate interface `IMetadataSerializer`, which has a default instance created on startup by implicitly. You can register a custom metadata serializer as a singleton or override the default one by calling `DefaultMetadataSerializer.SetDefaultSerializer` function.
