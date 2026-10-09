using System.Runtime.Serialization;
using System.Text.Json;
using Eventuous.Redis;

namespace Eventuous.Tests.Snapshots;

[ClassDataSource<SnapshotFixture>(Shared = SharedType.PerTestSession)]
public class FailureBoundaryTests(SnapshotFixture fixture) {
    [Test, Category("Reproduction")]
    [Arguments(EventBackend.Kurrent)]
    public async Task Deleted_domain_stream_with_leftover_snapshot_can_restore_stale_state(EventBackend backend) {
        var stream = Data.Stream();
        var events = Data.Events(3);
        var store = fixture.Events(backend);
        var snapshots = fixture.Store(Backend.Postgres);
        await store.Store(stream, ExpectedStreamVersion.NoStream, events);
        await snapshots.Write(stream, new() { Revision = 2, Payload = Data.Fold<External>(events).Snapshot() });
        await store.DeleteStream(stream, new ExpectedStreamVersion(2));
        (await store.StreamExists(stream)).ShouldBeFalse();
        // Characterizes failIfNotFound=false, the mode used by ExpectedState.Any commands.
        var loaded = await store.LoadState<LedgerState<External>>(stream, false, snapshots);
        Data.SameState(loaded.State, Data.Fold<External>(events));
        loaded.StreamVersion.Value.ShouldBe(2);
        await Should.ThrowAsync<StreamNotFound>(() => store.LoadState<LedgerState<External>>(stream, true, snapshots));
        await snapshots.Delete(stream);
        var empty = await store.LoadState<LedgerState<External>>(stream, false, snapshots);
        empty.StreamVersion.ShouldBe(ExpectedStreamVersion.NoStream);
        empty.Events.ShouldBeEmpty();
    }

    [Test, Category("Reproduction")]
    public async Task Postgres_domain_stream_deletion_is_not_implemented() {
        await Should.ThrowAsync<NotImplementedException>(() => fixture.Postgres.DeleteStream(Data.Stream(), ExpectedStreamVersion.Any));
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Cancelled_write_must_not_create_snapshot(Backend backend) {
        var stream = Data.Stream();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var store = fixture.Store(backend);
        var error = await RecordException(() => store.Write(stream,
            new() { Revision = 0, Payload = new LedgerSnapshot(1, 1, "a;") }, cancelled.Token));
        var persisted = await store.Read(stream);
        Console.WriteLine($"{backend}: pre-cancelled write exception={error?.GetType().Name ?? "none"}, persisted={persisted != null}");
        error.ShouldNotBeNull();
        error.ShouldBeAssignableTo<OperationCanceledException>();
        persisted.ShouldBeNull();
    }

    [Test, Category("Reproduction")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unreadable_redis_snapshot_blocks_replay_until_deleted(bool unknownType) {
        var stream = Data.Stream();
        var events = Data.Events(3);
        await fixture.Postgres.Store(stream, ExpectedStreamVersion.NoStream, events);
        var store = fixture.Store(Backend.Redis);
        await store.Write(stream, new() { Revision = 2, Payload = Data.Fold<External>(events).Snapshot() });
        await fixture.RedisDb.HashSetAsync($"snapshot:{stream}",
            unknownType ? EventuousRedisKeys.EventType : EventuousRedisKeys.JsonData,
            unknownType ? "snapshot-tests.unknown.v99" : "{invalid-json");
        var error = await RecordException(() => fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: store));
        if (unknownType) error.ShouldBeOfType<SerializationException>();
        else error.ShouldBeAssignableTo<JsonException>();
        await store.Delete(stream);
        var rebuilt = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: store);
        Data.SameState(rebuilt.State, Data.Fold<External>(events));
        rebuilt.StreamVersion.Value.ShouldBe(2);
    }

    [Test, Category("Contract")]
    [Arguments(EventBackend.Postgres, false)]
    [Arguments(EventBackend.Postgres, true)]
    [Arguments(EventBackend.Kurrent, false)]
    [Arguments(EventBackend.Kurrent, true)]
    public async Task Concurrent_commands_only_snapshot_the_successful_append(EventBackend backend, bool aggregate) {
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<External>, LedgerState<External>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        // Use an existing stream to isolate optimistic concurrency from PostgreSQL's
        // separate concurrent stream-creation/unique-key behavior.
        var store = fixture.Events(backend);
        await store.Store(stream, ExpectedStreamVersion.NoStream, new object[] { new Added(1, "seed") });
        var gated = new ConcurrentAppendGate(store);
        var snapshots = fixture.Store(Backend.Postgres);
        ICommandService<LedgerState<External>> service = aggregate
            ? new AggregateLedger<External>(gated, fixture.Types, snapshots, 1)
            : new FunctionalLedger<External>(gated, fixture.Types, snapshots, 1);
        var commandId = aggregate ? id : stream.ToString();
        var results = await Task.WhenAll(
            service.Handle(new Adjustment(commandId, 10, "a"), CancellationToken.None),
            service.Handle(new Adjustment(commandId, 20, "b"), CancellationToken.None));
        gated.Arrivals.ShouldBe(2);
        gated.ExpectedVersions.Order().ShouldBe(new long[] { 0, 0 });
        var raw = await store.ReadStream(stream, StreamReadPosition.Start);
        var expected = Data.Fold<External>(raw.Select(e => e.Payload!));
        var snapshot = await snapshots.Read(stream);
        var loaded = await store.LoadState<LedgerState<External>>(stream, snapshotStore: snapshots);
        Console.WriteLine($"{backend}, aggregate={aggregate}: both expected revision 0; successes={results.Count(r => r.Success)}, durable events={raw.Length}, full balance={expected.Balance}, snapshot revision={snapshot?.Revision}, loaded balance={loaded.State.Balance}");
        results.Count(r => r.Success).ShouldBe(1);
        results.Single(r => !r.Success).Exception.ShouldBeOfType<OptimisticConcurrencyException>();
        raw.Length.ShouldBe(2);
        snapshot.ShouldNotBeNull();
        snapshot.Revision.ShouldBe(1);
        snapshot.Payload.ShouldBe(expected.Snapshot());
        Data.SameState(loaded.State, expected);
        loaded.StreamVersion.Value.ShouldBe(1);
    }

    static async Task<Exception?> RecordException(Func<Task> action) {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
}

// Both commands finish loading the same revision before either real append starts.
public sealed class ConcurrentAppendGate(IEventStore inner) : IEventStore {
    readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _arrivals;
    public int Arrivals => _arrivals;
    public System.Collections.Concurrent.ConcurrentBag<long> ExpectedVersions { get; } = [];
    public async Task<AppendEventsResult> AppendEvents(StreamName stream, ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<NewStreamEvent> events, CancellationToken cancellationToken) {
        ExpectedVersions.Add(expectedVersion.Value);
        if (Interlocked.Increment(ref _arrivals) == 2) _ready.SetResult();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return await inner.AppendEvents(stream, expectedVersion, events, cancellationToken);
    }
    public Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, bool failIfNotFound, CancellationToken cancellationToken)
        => inner.ReadEvents(stream, start, count, failIfNotFound, cancellationToken);
    public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, bool failIfNotFound, CancellationToken cancellationToken)
        => inner.ReadEventsBackwards(stream, start, count, failIfNotFound, cancellationToken);
    public Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) => inner.StreamExists(stream, cancellationToken);
    public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default)
        => inner.DeleteStream(stream, expectedVersion, cancellationToken);
    public Task TruncateStream(StreamName stream, StreamTruncatePosition position, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default)
        => inner.TruncateStream(stream, position, expectedVersion, cancellationToken);
}
