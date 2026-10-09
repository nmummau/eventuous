namespace Eventuous.Tests.Snapshots;

[ClassDataSource<SnapshotFixture>(Shared = SharedType.PerTestSession)]
public class ReplayTests(SnapshotFixture fixture) {
    [Test, Category("Contract")]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SameStream, 1, 0)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SameStream, 1200, 1)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SameStream, 1200, 498)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SameStream, 1200, 499)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SeparateStream, 1, 0)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SeparateStream, 1000, 501)]
    [Arguments(EventBackend.Postgres, SnapshotStorageStrategy.SeparateStore, 1, 0)]
    [Arguments(EventBackend.Postgres, SnapshotStorageStrategy.SeparateStore, 1000, 501)]
    [Arguments(EventBackend.Kurrent, SnapshotStorageStrategy.SeparateStore, 1000, 501)]
    public Task Snapshot_plus_tail_matches_full_replay_and_revision(EventBackend backend, SnapshotStorageStrategy strategy, int covered, int tail)
        => strategy switch {
            SnapshotStorageStrategy.SameStream => Verify<Same>(backend, strategy, covered, tail),
            SnapshotStorageStrategy.SeparateStream => Verify<Separate>(backend, strategy, covered, tail),
            _ => Verify<External>(backend, strategy, covered, tail)
        };

    async Task Verify<T>(EventBackend backend, SnapshotStorageStrategy strategy, int covered, int tail) {
        var store = fixture.Events(backend);
        var stream = Data.Stream();
        var events = Data.Events(covered + tail);
        var snapshot = Data.Fold<T>(events.Take(covered)).Snapshot();
        object[] persisted = strategy == SnapshotStorageStrategy.SameStream
            ? [..events.Take(covered), snapshot, ..events.Skip(covered)] : events;
        var append = await store.Store(stream, ExpectedStreamVersion.NoStream, persisted);
        if (strategy == SnapshotStorageStrategy.SeparateStore)
            await fixture.Store(Backend.Postgres).Write(stream, new() { Revision = covered - 1, Payload = snapshot });
        if (strategy == SnapshotStorageStrategy.SeparateStream)
            await store.AppendEvents(StreamName.ForSnapshot(stream), ExpectedStreamVersion.NoStream,
                [new(Guid.NewGuid(), snapshot, new Metadata().With("revision", (covered - 1).ToString()))], CancellationToken.None);

        var loaded = await store.LoadState<LedgerState<T>>(stream, snapshotStore: fixture.Store(Backend.Postgres));
        Data.SameState(loaded.State, Data.Fold<T>(events));
        loaded.StreamVersion.Value.ShouldBe(append.NextExpectedVersion);
        loaded.Events.Length.ShouldBe(tail + 1);
        var aggregate = await store.LoadAggregate<Ledger<T>, LedgerState<T>>(stream, snapshotStore: fixture.Store(Backend.Postgres));
        Data.SameState(aggregate.State, loaded.State);
        aggregate.OriginalVersion.ShouldBe(append.NextExpectedVersion);
        // The loaded revision must be usable for the next optimistic append.
        var next = await store.Store(stream, loaded.StreamVersion, new object[] { new Added(7, "next") });
        next.NextExpectedVersion.ShouldBe(append.NextExpectedVersion + 1);
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task External_store_replays_tail_and_rebuilds_after_snapshot_deleted(Backend backend) {
        var stream = Data.Stream();
        var events = Data.Events(503);
        await fixture.Postgres.Store(stream, ExpectedStreamVersion.NoStream, events);
        var snapshots = fixture.Store(backend);
        await snapshots.Write(stream, new() { Revision = 499, Payload = Data.Fold<External>(events.Take(500)).Snapshot() });
        var optimized = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: snapshots);
        optimized.Events.Length.ShouldBe(4);
        optimized.StreamVersion.Value.ShouldBe(502);
        Data.SameState(optimized.State, Data.Fold<External>(events));
        await snapshots.Delete(stream);
        var rebuilt = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: snapshots);
        rebuilt.Events.Length.ShouldBe(503);
        Data.SameState(rebuilt.State, optimized.State);
        rebuilt.StreamVersion.ShouldBe(optimized.StreamVersion);
    }

    [Test, Category("Contract")]
    [Arguments(EventBackend.Postgres)]
    [Arguments(EventBackend.Kurrent)]
    public async Task Missing_stream_and_missing_snapshot_respect_fail_if_not_found(EventBackend backend) {
        var stream = Data.Stream();
        var snapshots = fixture.Store(Backend.Postgres);
        var loaded = await fixture.Events(backend).LoadState<LedgerState<External>>(stream, false, snapshots);
        loaded.Events.ShouldBeEmpty();
        loaded.StreamVersion.ShouldBe(ExpectedStreamVersion.NoStream);
        await Should.ThrowAsync<StreamNotFound>(() => fixture.Events(backend).LoadState<LedgerState<External>>(stream, true, snapshots));
    }

    [Test, Category("Contract")]
    public async Task Same_stream_selects_latest_snapshot_and_keeps_tail_order() {
        var stream = Data.Stream();
        var events = Data.Events(30);
        object[] persisted = [..events.Take(10), Data.Fold<Same>(events.Take(10)).Snapshot(),
            ..events.Skip(10).Take(10), Data.Fold<Same>(events.Take(20)).Snapshot(), ..events.Skip(20)];
        await fixture.Kurrent.Store(stream, ExpectedStreamVersion.NoStream, persisted);
        var loaded = await fixture.Kurrent.LoadState<LedgerState<Same>>(stream);
        loaded.Events.Length.ShouldBe(11);
        loaded.StreamVersion.Value.ShouldBe(31);
        Data.SameState(loaded.State, Data.Fold<Same>(events));
    }

    [Test, Category("Contract")]
    public async Task Snapshot_revision_is_used_when_there_are_no_tail_events() {
        var stream = Data.Stream();
        var events = Data.Events(750);
        await fixture.Postgres.Store(stream, ExpectedStreamVersion.NoStream, events);
        await fixture.Store(Backend.Postgres).Write(stream, new() { Revision = 749, Payload = Data.Fold<External>(events).Snapshot() });
        var loaded = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: fixture.Store(Backend.Postgres));
        loaded.StreamVersion.Value.ShouldBe(749);
        loaded.Events.Length.ShouldBe(1);
        await Should.ThrowAsync<OptimisticConcurrencyException>(() => fixture.Postgres.Store(stream, new ExpectedStreamVersion(748), new object[] { new Added(1, "stale") }));
    }

    [Test, Category("KnownDefect")]
    [Arguments(500)]
    [Arguments(501)]
    [Arguments(1000)]
    public async Task Same_stream_snapshot_beyond_first_page_must_replay_without_repeating_pages(int tail) {
        var stream = Data.Stream();
        var events = Data.Events(5 + tail);
        object[] persisted = [..events.Take(5), Data.Fold<Same>(events.Take(5)).Snapshot(), ..events.Skip(5)];
        await fixture.Kurrent.Store(stream, ExpectedStreamVersion.NoStream, persisted);
        var bounded = new BoundedReader(fixture.Kurrent);
        var loaded = await bounded.LoadState<LedgerState<Same>>(stream);
        Data.SameState(loaded.State, Data.Fold<Same>(events));
        bounded.Positions[1].ShouldBe(persisted.Length - 501L);
    }

    [Test, Category("KnownDefect")]
    [Arguments(500)]
    [Arguments(501)]
    [Arguments(1001)]
    public async Task No_snapshot_must_fall_back_to_complete_multi_page_replay(int count) {
        var stream = Data.Stream();
        var events = Data.Events(count);
        await fixture.Kurrent.Store(stream, ExpectedStreamVersion.NoStream, events);
        var loaded = await new BoundedReader(fixture.Kurrent).LoadState<LedgerState<Same>>(stream);
        Data.SameState(loaded.State, Data.Fold<Same>(events));
    }

    [Test, Category("Contract")]
    [Arguments(SnapshotStorageStrategy.SameStream)]
    [Arguments(SnapshotStorageStrategy.SeparateStream)]
    public Task Postgres_supports_snapshot_backward_reads(SnapshotStorageStrategy strategy)
        => strategy == SnapshotStorageStrategy.SameStream
            ? Verify<Same>(EventBackend.Postgres, strategy, 10, 2)
            : Verify<Separate>(EventBackend.Postgres, strategy, 10, 2);
}
