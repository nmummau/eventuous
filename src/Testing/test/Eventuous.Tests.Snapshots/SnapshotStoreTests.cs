namespace Eventuous.Tests.Snapshots;

[ClassDataSource<SnapshotFixture>(Shared = SharedType.PerTestSession)]
public class SnapshotStoreTests(SnapshotFixture fixture) {
    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Missing_snapshot_returns_null(Backend backend) {
        (await fixture.Store(backend).Read(Data.Stream())).ShouldBeNull();
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Roundtrip_preserves_payload_type_unicode_decimal_and_large_revision(Backend backend) {
        var stream = Data.Stream();
        var snapshot = new Snapshot { Revision = (long)int.MaxValue + 42, Payload = new LedgerSnapshot(1234.5678m, 17, "日本語;🌾;quoted-\";\\path;") };
        await fixture.Store(backend).Write(stream, snapshot);
        var loaded = await fixture.Store(backend).Read(stream);
        loaded.ShouldNotBeNull();
        loaded.Revision.ShouldBe(snapshot.Revision);
        loaded.Payload.ShouldBeOfType<LedgerSnapshot>().ShouldBe(snapshot.Payload);
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Newer_snapshot_replaces_old_snapshot_as_one_record(Backend backend) {
        var store = fixture.Store(backend);
        var stream = Data.Stream();
        await store.Write(stream, new() { Revision = 0, Payload = new LedgerSnapshot(1, 1, "old;") });
        await store.Write(stream, new() { Revision = 10, Payload = new LedgerSnapshot(20, 11, "new;") });
        var loaded = await store.Read(stream);
        loaded.ShouldNotBeNull();
        loaded.Revision.ShouldBe(10);
        loaded.Payload.ShouldBe(new LedgerSnapshot(20, 11, "new;"));
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Same_revision_write_is_idempotent(Backend backend) {
        var stream = Data.Stream();
        var snapshot = new Snapshot { Revision = 5, Payload = new LedgerSnapshot(15, 6, "same;") };
        await fixture.Store(backend).Write(stream, snapshot);
        await fixture.Store(backend).Write(stream, snapshot);
        (await fixture.Store(backend).Read(stream)).ShouldBe(snapshot);
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Delete_is_idempotent_and_does_not_delete_other_streams(Backend backend) {
        var store = fixture.Store(backend);
        var first = Data.Stream();
        var other = Data.Stream();
        var snapshot = new Snapshot { Revision = 0, Payload = new LedgerSnapshot(1, 1, "a;") };
        await store.Write(first, snapshot);
        await store.Write(other, snapshot);
        await store.Delete(first);
        await store.Delete(first);
        (await store.Read(first)).ShouldBeNull();
        (await store.Read(other)).ShouldBe(snapshot);
    }

    [Test, Category("Contract")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Null_payload_is_rejected_without_creating_snapshot(Backend backend) {
        var stream = Data.Stream();
        await Should.ThrowAsync<ArgumentException>(() => fixture.Store(backend).Write(stream, new() { Revision = 0, Payload = null }));
        (await fixture.Store(backend).Read(stream)).ShouldBeNull();
    }

    [Test, Category("KnownDefect")]
    [Arguments(Backend.Postgres)]
    [Arguments(Backend.Redis)]
    [Arguments(Backend.Mongo)]
    [Arguments(Backend.SqlServer)]
    public async Task Delayed_older_writer_must_not_replace_newer_snapshot(Backend backend) {
        var store = fixture.Store(backend);
        var stream = Data.Stream();
        var olderWriteReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOlderWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = Task.Run(async () => {
            olderWriteReady.SetResult();
            await releaseOlderWrite.Task;
            await store.Write(stream, new() { Revision = 100, Payload = new LedgerSnapshot(100, 101, "old;") });
        });
        await olderWriteReady.Task;
        try { await store.Write(stream, new() { Revision = 110, Payload = new LedgerSnapshot(110, 111, "new;") }); }
        finally { releaseOlderWrite.SetResult(); }
        await older;
        var loaded = await store.Read(stream);
        loaded.ShouldNotBeNull();
        Console.WriteLine($"{backend}: newer revision 110 committed before delayed revision 100; stored revision={loaded.Revision}");
        loaded.Revision.ShouldBe(110, "Snapshot writes should be monotonic in represented stream revision.");
    }
}
