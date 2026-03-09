# Multi-Stream Produce Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add multi-stream `Produce` overloads to `IProducer` / `IProducer<T>` for batch efficiency, and update Gateway to use them.

**Architecture:** New `ProduceRequest` / `ProduceRequest<T>` record structs as input types. Default interface methods on `IProducer` / `IProducer<T>` with parallel execution. `BaseProducer<T>` overrides with tracing. Gateway simplified to single multi-stream call.

**Tech Stack:** C# preview, .NET 10/9/8, TUnit test framework, OpenTelemetry tracing

---

### Task 1: Add ProduceRequest Types

**Files:**
- Create: `src/Core/src/Eventuous.Producers/ProduceRequest.cs`

**Step 1: Create the ProduceRequest types file**

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;

namespace Eventuous.Producers;

[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest(StreamName Stream, IEnumerable<ProducedMessage> Messages);

[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest<TProduceOptions>(StreamName Stream, IEnumerable<ProducedMessage> Messages, TProduceOptions? Options)
    where TProduceOptions : class;
```

**Step 2: Verify it builds**

Run: `dotnet build src/Core/src/Eventuous.Producers/Eventuous.Producers.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: add ProduceRequest types for multi-stream produce
```

---

### Task 2: Add Multi-Stream Overloads to IProducer Interfaces

**Files:**
- Modify: `src/Core/src/Eventuous.Producers/IProducer.cs`

**Step 1: Add default interface method to IProducer**

After the existing `Produce` method (line 18), add:

```csharp
/// <summary>
/// Produce messages to multiple streams in parallel.
/// </summary>
/// <param name="requests">Collection of produce requests, one per target stream</param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
[RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
[RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
Task Produce(IReadOnlyCollection<ProduceRequest> requests, CancellationToken cancellationToken = default)
    => Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, cancellationToken)));
```

**Step 2: Add default interface method to IProducer<TProduceOptions>**

After the existing `Produce` method (line 33), add:

```csharp
/// <summary>
/// Produce messages to multiple streams in parallel.
/// </summary>
/// <param name="requests">Collection of produce requests with options, one per target stream</param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
[RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
[RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
Task Produce(IReadOnlyCollection<ProduceRequest<TProduceOptions>> requests, CancellationToken cancellationToken = default)
    => Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, r.Options, cancellationToken)));
```

**Step 3: Verify it builds**

Run: `dotnet build src/Core/src/Eventuous.Producers/Eventuous.Producers.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
feat: add multi-stream Produce overloads to IProducer interfaces
```

---

### Task 3: Add Multi-Stream Produce to BaseProducer with Tracing

**Files:**
- Modify: `src/Core/src/Eventuous.Producers/BaseProducer.cs`

**Step 1: Add multi-stream Produce override with batch tracing**

After the existing single-stream `Produce` method (ends at line 56), add:

```csharp
/// <inheritdoc />
[RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
[RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
public Task Produce(IReadOnlyCollection<ProduceRequest<TProduceOptions>> requests, CancellationToken cancellationToken = default) {
    if (requests.Count == 0) return Task.CompletedTask;

    return Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, r.Options, cancellationToken)));
}
```

Note: Each individual `Produce(stream, messages, options, ct)` call already creates its own tracing activity via the existing single-stream `Produce` method on `BaseProducer`. So multi-stream just fans out to the already-traced single-stream calls.

Also add the non-generic overload:

```csharp
[RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
[RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
public Task Produce(IReadOnlyCollection<ProduceRequest> requests, CancellationToken cancellationToken = default) {
    if (requests.Count == 0) return Task.CompletedTask;

    return Task.WhenAll(requests.Select(r => Produce(r.Stream, r.Messages, cancellationToken)));
}
```

**Step 2: Build the solution**

Run: `dotnet build Eventuous.slnx`
Expected: Build succeeded. This validates all producer implementations (Kafka, RabbitMQ, PubSub, Service Bus) still compile — they inherit from `BaseProducer<T>` and don't need changes.

**Step 3: Commit**

```
feat: add multi-stream Produce to BaseProducer with empty-check
```

---

### Task 4: Update GatewayProducer to Delegate Multi-Stream

**Files:**
- Modify: `src/Gateway/src/Eventuous.Gateway/GatewayProducer.cs`

**Step 1: Add multi-stream Produce delegation**

Add two new methods to `GatewayProducer<T>` that delegate to the inner producer after waiting for readiness:

```csharp
public async Task Produce(IReadOnlyCollection<ProduceRequest<T>> requests, CancellationToken cancellationToken = default) {
    if (_isHostedService) { await WaitForInner(inner, cancellationToken).NoContext(); }

    await inner.Produce(requests, cancellationToken).NoContext();
}

public async Task Produce(IReadOnlyCollection<ProduceRequest> requests, CancellationToken cancellationToken = default) {
    if (_isHostedService) { await WaitForInner(inner, cancellationToken).NoContext(); }

    await ((IProducer)inner).Produce(requests, cancellationToken).NoContext();
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/Gateway/src/Eventuous.Gateway/Eventuous.Gateway.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: add multi-stream Produce delegation to GatewayProducer
```

---

### Task 5: Update GatewayHandler to Use Multi-Stream Produce

**Files:**
- Modify: `src/Gateway/src/Eventuous.Gateway/GatewayHandler.cs`

**Step 1: Replace GroupBy + parallel Produce with single multi-stream call**

Replace the `HandleEvent` try block (lines 37-41) with:

```csharp
try {
    var contextMeta = GatewayMetaHelper.GetContextMeta(context);

    var requests = shovelMessages
        .GroupBy(x => x.TargetStream)
        .Select(g => new ProduceRequest<TProduceOptions>(
            g.Key,
            g.Select(x => new ProducedMessage(x.Message, x.GetMeta(context), contextMeta) { OnAck = onAck, OnNack = onFail }),
            g.First().ProduceOptions
        ))
        .ToArray();

    await producer.Produce(requests, context.CancellationToken).NoContext();
} catch (OperationCanceledException e) { context.Nack<GatewayHandler<TProduceOptions>>(e); }
```

This eliminates the `ProduceToStream` local function entirely. The `ProduceRequest` constructor bundles stream + messages + options, and the multi-stream `Produce` handles parallel execution.

Note on `ProduceOptions`: Currently the old code calls `Produce` per message (each with its own `ProduceOptions`). Grouping by stream means we pick `g.First().ProduceOptions` for the group. This is consistent with the existing behavior — messages to the same stream should have the same options. If different options per message within a stream are needed, that's a separate concern handled at the single-stream level.

**Step 2: Verify it builds**

Run: `dotnet build src/Gateway/src/Eventuous.Gateway/Eventuous.Gateway.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
refactor: simplify GatewayHandler using multi-stream Produce
```

---

### Task 6: Write Tests for Multi-Stream Produce

**Files:**
- Create: `src/Gateway/test/Eventuous.Tests.Gateway/MultiStreamProduceTests.cs`

**Step 1: Write the test class**

Create a test file that validates multi-stream produce behavior using a test producer (similar pattern to `RegistrationTests.cs`):

```csharp
using Eventuous.Producers;

namespace Eventuous.Tests.Gateway;

public class MultiStreamProduceTests {
    [Test]
    public async Task ShouldProduceToMultipleStreams() {
        var producer = new TestProducer();
        var stream1 = new StreamName("stream-1");
        var stream2 = new StreamName("stream-2");
        var msg1 = new ProducedMessage("event-1", null);
        var msg2 = new ProducedMessage("event-2", null);

        var requests = new ProduceRequest<TestProduceOptions>[] {
            new(stream1, [msg1], null),
            new(stream2, [msg2], null)
        };

        await producer.Produce(requests);

        await Assert.That(producer.ProducedMessages).HasCount().EqualTo(2);
        await Assert.That(producer.Streams).Contains(stream1);
        await Assert.That(producer.Streams).Contains(stream2);
    }

    [Test]
    public async Task ShouldHandleEmptyRequests() {
        var producer = new TestProducer();

        await producer.Produce(Array.Empty<ProduceRequest<TestProduceOptions>>());

        await Assert.That(producer.ProducedMessages).HasCount().EqualTo(0);
    }

    [Test]
    public async Task ShouldProduceMultipleMessagesToSameStream() {
        var producer = new TestProducer();
        var stream = new StreamName("stream-1");
        var msg1 = new ProducedMessage("event-1", null);
        var msg2 = new ProducedMessage("event-2", null);

        var requests = new ProduceRequest<TestProduceOptions>[] {
            new(stream, [msg1, msg2], null)
        };

        await producer.Produce(requests);

        await Assert.That(producer.ProducedMessages).HasCount().EqualTo(2);
        await Assert.That(producer.Streams.Distinct()).HasCount().EqualTo(1);
    }

    class TestProducer : BaseProducer<TestProduceOptions> {
        public List<ProducedMessage> ProducedMessages { get; } = [];
        public List<StreamName> Streams { get; } = [];

        protected override Task ProduceMessages(
                StreamName                   stream,
                IEnumerable<ProducedMessage> messages,
                TestProduceOptions?          options,
                CancellationToken            cancellationToken = default
            ) {
            Streams.Add(stream);
            ProducedMessages.AddRange(messages);

            return Task.CompletedTask;
        }
    }

    record TestProduceOptions;
}
```

**Step 2: Run the tests**

Run: `dotnet test --project src/Gateway/test/Eventuous.Tests.Gateway/Eventuous.Tests.Gateway.csproj -f net10.0`
Expected: All tests pass

**Step 3: Commit**

```
test: add multi-stream produce tests
```

---

### Task 7: Build and Run Full Test Suite

**Step 1: Build entire solution**

Run: `dotnet build Eventuous.slnx`
Expected: Build succeeded, 0 errors

**Step 2: Run Gateway tests**

Run: `dotnet test --project src/Gateway/test/Eventuous.Tests.Gateway/Eventuous.Tests.Gateway.csproj -f net10.0`
Expected: All tests pass

**Step 3: Run core tests to verify no regressions**

Run: `dotnet test --project src/Core/test/Eventuous.Tests/Eventuous.Tests.csproj -f net10.0`
Expected: All tests pass

**Step 4: Final commit if any cleanup needed, then push**
