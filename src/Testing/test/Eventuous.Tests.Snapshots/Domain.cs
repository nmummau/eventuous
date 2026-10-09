namespace Eventuous.Tests.Snapshots;

public record Added(decimal Amount, string Token);
public record LedgerSnapshot(decimal Balance, int Applied, string History);
public record Adjustment(string Id, decimal Amount, string Token);
public record LedgerId(string Value) : Id(Value);
public sealed class Same;
public sealed class Separate;
public sealed class External;

public record LedgerState<TPolicy> : State<LedgerState<TPolicy>> {
    public decimal Balance { get; init; }
    public int Applied { get; init; }
    public string History { get; init; } = "";

    public LedgerState() {
        On<Added>((s, e) => s with { Balance = s.Balance + e.Amount, Applied = s.Applied + 1, History = s.History + e.Token + ";" });
        On<LedgerSnapshot>((s, e) => s with { Balance = e.Balance, Applied = e.Applied, History = e.History });
    }

    public LedgerSnapshot Snapshot() => new(Balance, Applied, History);
}

public class Ledger<TPolicy> : Aggregate<LedgerState<TPolicy>> {
    public void Add(decimal amount, string token) => Apply(new Added(amount, token));
}

public class FunctionalLedger<TPolicy> : CommandService<LedgerState<TPolicy>> {
    public FunctionalLedger(IEventStore store, ITypeMapper types, ISnapshotStore? snapshots, int threshold = 3)
        : base(store, typeMap: types, snapshotStore: snapshots) {
        UseSnapshotStrategy((events, _) => events.OfType<Added>().Count() >= threshold, (_, state) => state.Snapshot());
        On<Adjustment>().InState(ExpectedState.Any)
            .GetStream(cmd => new StreamName(cmd.Id))
            .Act((_, _, cmd) => cmd.Amount == 0 ? [] : new object[] { new Added(cmd.Amount, cmd.Token) });
    }
}

public class AggregateLedger<TPolicy> : CommandService<Ledger<TPolicy>, LedgerState<TPolicy>, LedgerId> {
    public AggregateLedger(IEventStore store, ITypeMapper types, ISnapshotStore? snapshots, int threshold = 3)
        : base(store, typeMap: types, snapshotStore: snapshots) {
        UseSnapshotStrategy((events, _) => events.OfType<Added>().Count() >= threshold, (_, state) => state.Snapshot());
        On<Adjustment>().InState(ExpectedState.Any).GetId(cmd => new LedgerId(cmd.Id))
            .Act((ledger, cmd) => { if (cmd.Amount != 0) ledger.Add(cmd.Amount, cmd.Token); });
    }
}

public static class Data {
    public static StreamName Stream() => new($"SnapshotTest-{Guid.NewGuid():N}");
    public static object[] Events(int count) => Enumerable.Range(0, count).Select(i => (object)new Added(i % 2 == 0 ? 1.25m : -0.5m, $"e{i}")).ToArray();
    public static LedgerState<T> Fold<T>(IEnumerable<object> events) => events.Aggregate(new LedgerState<T>(), (s, e) => s.When(e));
    public static void SameState<T>(LedgerState<T> actual, LedgerState<T> expected) {
        actual.Balance.ShouldBe(expected.Balance);
        actual.Applied.ShouldBe(expected.Applied);
        actual.History.ShouldBe(expected.History);
    }
}
