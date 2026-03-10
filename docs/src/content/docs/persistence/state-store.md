---
title: "State store"
description: "Loading state from event streams"
sidebar:
  order: 7
---

The `LoadState` extension method on `IEventReader` reads an event stream and folds it into a `State<T>` instance. Unlike `LoadAggregate`, it does not require an aggregate — it works directly with state types, making it the foundation for both the aggregate-based and [functional](../../application/func-service) command services.

## Loading state by stream name

The simplest overload takes a stream name and returns a `FoldedEventStream<TState>`:

```csharp
var result = await eventReader.LoadState<BookingState>(
    new StreamName("Booking-123"),
    cancellationToken: cancellationToken
);

var state   = result.State;         // the folded state instance
var version = result.StreamVersion; // for optimistic concurrency on subsequent writes
var events  = result.Events;        // the raw event objects
```

By default, `LoadState` throws `StreamNotFound` if the stream doesn't exist. Pass `failIfNotFound: false` to get an empty state instead:

```csharp
var result = await eventReader.LoadState<BookingState>(
    new StreamName("Booking-123"),
    failIfNotFound: false,
    cancellationToken: cancellationToken
);

// result.State is a new BookingState() with no events applied
// result.StreamVersion is ExpectedStreamVersion.NoStream
```

## Loading state by identity

When you have a typed identity derived from `Id`, use the overload that takes a `StreamNameMap` and an id. This resolves the stream name using the registered mapping (or the default convention) and automatically sets the `Id` property on `State<TState, TId>` types:

```csharp
var streamNameMap = new StreamNameMap();

var result = await eventReader.LoadState<BookingState, BookingId>(
    streamNameMap,
    new BookingId("123"),
    cancellationToken: cancellationToken
);

// result.State.Id is set to the BookingId("123") instance
```

## FoldedEventStream

`LoadState` returns a `FoldedEventStream<TState>` record that contains everything needed for subsequent operations:

| Property        | Type                     | Description                                            |
|-----------------|--------------------------|--------------------------------------------------------|
| `State`         | `TState`                 | The state instance built by folding all stream events. |
| `StreamName`    | `StreamName`             | The stream the events were read from.                  |
| `StreamVersion` | `ExpectedStreamVersion`  | The version of the last event in the stream.           |
| `Events`        | `object[]`               | The deserialized event payloads.                       |

The `StreamVersion` value is used for optimistic concurrency when appending new events to the same stream:

```csharp
var loaded = await eventReader.LoadState<BookingState>(streamName);

// ... produce new events based on the state ...

await eventWriter.Store(
    loaded.StreamName,
    loaded.StreamVersion,
    newEvents,
    cancellationToken: cancellationToken
);
```

## How it works

`LoadState` reads the full stream using `ReadStream` (which handles pagination internally), then folds the events into a new state instance by calling `State<T>.When(event)` for each event in sequence. The fold produces an immutable state object that reflects the entire stream history.

This is the same mechanism used internally by:
- The **functional command service** (`CommandService<TState>`) to load state before executing command handlers
- The **aggregate command service** (`CommandService<TAggregate, TState, TId>`) via `LoadAggregate`, which wraps `LoadState` and attaches the result to an aggregate instance
