using System.Reflection;
using Eventuous.Redis.Snapshots;
using StackExchange.Redis;

namespace Eventuous.Tests.Snapshots;

[ClassDataSource<SnapshotFixture>(Shared = SharedType.PerTestSession)]
public class RedisConsistencyTests(SnapshotFixture fixture) {
    [Test, Category("KnownDefect")]
    public async Task Concurrent_replacement_must_not_mix_revision_and_payload_or_double_replay() {
        var stream = Data.Stream();
        var events = Data.Events(11);
        await fixture.Postgres.Store(stream, ExpectedStreamVersion.NoStream, events);
        var realStore = fixture.Store(Backend.Redis);
        await realStore.Write(stream, new() { Revision = 9, Payload = Data.Fold<External>(events.Take(10)).Snapshot() });
        var proxy = DispatchProxy.Create<IDatabase, RedisReadGate>();
        var gate = (RedisReadGate)(object)proxy;
        gate.Inner = fixture.RedisDb;
        gate.ReplaceSnapshot = () => realStore.Write(stream, new() { Revision = 10, Payload = Data.Fold<External>(events).Snapshot() });
        var gatedStore = new RedisSnapshotStore(() => proxy, serializer: fixture.Serializer);
        var actual = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: gatedStore);
        var expected = Data.Fold<External>(events);
        gate.Replacements.ShouldBe(1, "The test must execute the intended read/write interleaving.");
        Console.WriteLine($"Redis interleaving: expected applied={expected.Applied}, balance={expected.Balance}; actual applied={actual.State.Applied}, balance={actual.State.Balance}");
        actual.State.Applied.ShouldBe(expected.Applied, "Snapshot revision 9 must not be paired with revision-10 state, replaying event 10 twice.");
        Data.SameState(actual.State, expected);
    }

    [Test, Category("Contract")]
    public async Task Redis_read_without_replacement_preserves_state_and_revision() {
        var stream = Data.Stream();
        var snapshot = new Snapshot { Revision = 9, Payload = new LedgerSnapshot(10, 10, "control;") };
        var store = fixture.Store(Backend.Redis);
        await store.Write(stream, snapshot);
        (await store.Read(stream)).ShouldBe(snapshot);
    }
}
