namespace Eventuous.Tests.Snapshots;

[ClassDataSource<SnapshotFixture>(Shared = SharedType.PerTestSession)]
public class CommandServiceTests(SnapshotFixture fixture) {
    [Test, Category("Contract")]
    [Arguments(Backend.Postgres, false)]
    [Arguments(Backend.Postgres, true)]
    [Arguments(Backend.Redis, false)]
    [Arguments(Backend.Redis, true)]
    [Arguments(Backend.Mongo, false)]
    [Arguments(Backend.Mongo, true)]
    [Arguments(Backend.SqlServer, false)]
    [Arguments(Backend.SqlServer, true)]
    public async Task Repeated_commands_snapshot_every_three_events_and_preserve_state(Backend backend, bool aggregate) {
        var snapshots = fixture.Store(backend);
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<External>, LedgerState<External>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        ICommandService<LedgerState<External>> service = aggregate
            ? new AggregateLedger<External>(fixture.Postgres, fixture.Types, snapshots)
            : new FunctionalLedger<External>(fixture.Postgres, fixture.Types, snapshots);
        var commandId = aggregate ? id : stream.ToString();
        for (var i = 1; i <= 7; i++) {
            var result = await service.Handle(new Adjustment(commandId, i, $"c{i}"), CancellationToken.None);
            result.ThrowIfError();
            result.Get()!.State.Balance.ShouldBe(i * (i + 1) / 2m);
            var snapshot = await snapshots.Read(stream);
            if (i < 3) snapshot.ShouldBeNull();
            else { snapshot.ShouldNotBeNull(); snapshot.Revision.ShouldBe(i < 6 ? 2 : 5); }
        }
        var loaded = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: snapshots);
        loaded.State.Balance.ShouldBe(28);
        loaded.State.Applied.ShouldBe(7);
        loaded.State.History.ShouldBe("c1;c2;c3;c4;c5;c6;c7;");
        loaded.StreamVersion.Value.ShouldBe(6);
        loaded.Events.Length.ShouldBe(2);
        var noop = await service.Handle(new Adjustment(commandId, 0, "ignored"), CancellationToken.None);
        noop.ThrowIfError();
        noop.Get()!.Changes.ShouldBeEmpty();
        (await fixture.Postgres.ReadStream(stream, StreamReadPosition.Start)).Length.ShouldBe(7);
    }

    [Test, Category("Contract")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Same_stream_command_snapshots_share_the_domain_append(bool aggregate) {
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<Same>, LedgerState<Same>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        ICommandService<LedgerState<Same>> service = aggregate
            ? new AggregateLedger<Same>(fixture.Kurrent, fixture.Types, null)
            : new FunctionalLedger<Same>(fixture.Kurrent, fixture.Types, null);
        for (var i = 1; i <= 7; i++)
            (await service.Handle(new Adjustment(aggregate ? id : stream.ToString(), i, $"c{i}"), CancellationToken.None)).ThrowIfError();
        var raw = await fixture.Kurrent.ReadStream(stream, StreamReadPosition.Start);
        raw.Length.ShouldBe(9);
        raw.Count(e => e.Payload is LedgerSnapshot).ShouldBe(2);
        var loaded = await fixture.Kurrent.LoadState<LedgerState<Same>>(stream);
        loaded.State.Balance.ShouldBe(28);
        loaded.State.Applied.ShouldBe(7);
        loaded.StreamVersion.Value.ShouldBe(8);
        loaded.Events.Length.ShouldBe(2);
    }

    [Test, Category("Contract")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Separate_stream_command_snapshots_record_domain_revision_and_keep_latest(bool aggregate) {
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<Separate>, LedgerState<Separate>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        ICommandService<LedgerState<Separate>> service = aggregate
            ? new AggregateLedger<Separate>(fixture.Kurrent, fixture.Types, null)
            : new FunctionalLedger<Separate>(fixture.Kurrent, fixture.Types, null);
        for (var i = 1; i <= 7; i++)
            (await service.Handle(new Adjustment(aggregate ? id : stream.ToString(), i, $"c{i}"), CancellationToken.None)).ThrowIfError();
        var snapshotEvents = await fixture.Kurrent.ReadEventsBackwards(StreamName.ForSnapshot(stream), StreamReadPosition.End, 10, true, CancellationToken.None);
        snapshotEvents.Length.ShouldBe(1);
        snapshotEvents[0].Metadata.GetString("revision").ShouldBe("5");
        var loaded = await fixture.Kurrent.LoadState<LedgerState<Separate>>(stream);
        loaded.State.Balance.ShouldBe(28);
        loaded.State.Applied.ShouldBe(7);
        loaded.StreamVersion.Value.ShouldBe(6);
        loaded.Events.Length.ShouldBe(2);
    }

    // These are characterizations, not assertions that failure-after-commit is desirable.
    [Test, Category("Reproduction")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Snapshot_write_failure_reports_error_after_commit_and_retry_duplicates_business_event(bool aggregate) {
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<External>, LedgerState<External>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        var failing = new FailingSnapshotStore(fixture.Store(Backend.Postgres));
        ICommandService<LedgerState<External>> service = aggregate
            ? new AggregateLedger<External>(fixture.Postgres, fixture.Types, failing, 1)
            : new FunctionalLedger<External>(fixture.Postgres, fixture.Types, failing, 1);
        var command = new Adjustment(aggregate ? id : stream.ToString(), 10, "deposit");
        var first = await service.Handle(command, CancellationToken.None);
        first.Success.ShouldBeFalse();
        first.Exception.ShouldBeOfType<IOException>();
        var committed = await fixture.Postgres.ReadStream(stream, StreamReadPosition.Start);
        committed.Length.ShouldBe(1);
        committed[0].Payload.ShouldBe(new Added(10, "deposit"));
        var retry = await service.Handle(command, CancellationToken.None);
        retry.Success.ShouldBeFalse();
        var reloaded = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: fixture.Store(Backend.Postgres));
        reloaded.State.Balance.ShouldBe(20);
        reloaded.State.Applied.ShouldBe(2);
        Console.WriteLine($"aggregate={aggregate}: first command reported failure with 1 committed event; retry produced balance 20 from deposit 10.");
    }

    [Test, Category("Contract")]
    public async Task Missing_external_store_fails_before_any_event_is_committed() {
        var stream = Data.Stream();
        var service = new FunctionalLedger<External>(fixture.Postgres, fixture.Types, null);
        var result = await service.Handle(new Adjustment(stream, 10, "a"), CancellationToken.None);
        result.Success.ShouldBeFalse();
        result.Exception.ShouldBeOfType<InvalidOperationException>();
        (await fixture.Postgres.StreamExists(stream)).ShouldBeFalse();
    }

    [Test, Category("KnownDefect")]
    [Arguments(Backend.Postgres, false)]
    [Arguments(Backend.Postgres, true)]
    [Arguments(Backend.Redis, false)]
    [Arguments(Backend.Redis, true)]
    [Arguments(Backend.Mongo, false)]
    [Arguments(Backend.Mongo, true)]
    [Arguments(Backend.SqlServer, false)]
    [Arguments(Backend.SqlServer, true)]
    public async Task Snapshot_only_command_must_preserve_existing_stream_revision(Backend backend, bool aggregate) {
        var id = Guid.NewGuid().ToString("N");
        var stream = aggregate ? StreamNameFactory.For<Ledger<External>, LedgerState<External>, LedgerId>(new(id)) : new StreamName($"Ledger-{id}");
        var events = Data.Events(3);
        await fixture.Postgres.Store(stream, ExpectedStreamVersion.NoStream, events);
        var snapshots = fixture.Store(backend);
        ICommandService<LedgerState<External>> service = aggregate
            ? new AggregateLedger<External>(fixture.Postgres, fixture.Types, snapshots, 0)
            : new FunctionalLedger<External>(fixture.Postgres, fixture.Types, snapshots, 0);
        var result = await service.Handle(new Adjustment(aggregate ? id : stream.ToString(), 0, "noop"), CancellationToken.None);
        Console.WriteLine($"{backend}, aggregate={aggregate}: snapshot-only command success={result.Success}, error={result.Exception?.Message}");
        result.ThrowIfError();
        var snapshot = await snapshots.Read(stream);
        snapshot.ShouldNotBeNull();
        Console.WriteLine($"Snapshot-only command: snapshot revision={snapshot.Revision}; actual domain revision=2");
        snapshot.Revision.ShouldBe(2, "An empty domain append must not reset the represented revision to AppendEventsResult.NoOp's value.");
    }

    [Test, Category("KnownDefect")]
    public async Task Explicit_snapshot_inside_batch_must_not_skip_following_domain_event() {
        var stream = Data.Stream();
        var snapshots = fixture.Store(Backend.Postgres);
        var service = new MidBatchService(fixture.Postgres, fixture.Types, snapshots);
        var result = await service.Handle(new Adjustment(stream, 10, "a"), CancellationToken.None);
        result.ThrowIfError();
        result.Get()!.State.Balance.ShouldBe(15);
        var loaded = await fixture.Postgres.LoadState<LedgerState<External>>(stream, snapshotStore: snapshots);
        Console.WriteLine($"Mid-batch snapshot: command result balance=15; reloaded balance={loaded.State.Balance}");
        loaded.State.Balance.ShouldBe(15, "The snapshot precedes the final event and must not claim to include it.");
    }

    sealed class MidBatchService : CommandService<LedgerState<External>> {
        public MidBatchService(IEventStore store, ITypeMapper types, ISnapshotStore snapshots) : base(store, typeMap: types, snapshotStore: snapshots) {
            On<Adjustment>().InState(ExpectedState.New).GetStream(cmd => new StreamName(cmd.Id))
                .Act((_, _, _) => new object[] { new Added(10, "a"), new LedgerSnapshot(10, 1, "a;"), new Added(5, "b") });
        }
    }
}
