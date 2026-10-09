# Snapshot validation

Tests the unchanged snapshot implementation at commit
`a06a726ea6eef2186d4cc077220e081647c772eb` (PR #479). This is a baseline
evaluation, not a production fix.

The [evidence directory](evidence/) archives the five reports summarized in
[RESULTS.md](RESULTS.md), with their original bytes and SHA-256 hashes. New
test runs write to the ignored results directory without overwriting this archive.

## Run

From the repository root, with .NET 10 and access to the Docker daemon:

```bash
dotnet build src/Testing/test/Eventuous.Tests.Snapshots/Eventuous.Tests.Snapshots.csproj -f net10.0 --nologo
dotnet run --project src/Testing/test/Eventuous.Tests.Snapshots/Eventuous.Tests.Snapshots.csproj -f net10.0 --no-build -- \
  --report-trx-filename snapshots-final.trx \
  --maximum-parallel-tests 8 --timeout 10m --no-progress --no-ansi
```

The repository supplies `--report-trx` and the results directory automatically.
Do not pass a second `--results-directory`. Reports go to
`test-results/net10.0/`. Red baseline tests make the runner exit with code 2.
Run outside a filesystem/network sandbox that blocks Docker's socket; a working
Docker CLI does not imply a sandboxed test process can access it.

To isolate a class, add `--treenode-filter '/*/*/GeneratorTests/*'` (or another
class name). Generator tests do not need running containers.

The verified category filter is `--treenode-filter '/*/*/*/*[Category=Reproduction]'`.
That focused run passes all six cases; these passing tests establish the
documented undesirable behaviors rather than endorsing them.

## Coverage and interpretation

The fixture creates five disposable containers with random ports: PostgreSQL 14,
Redis 7.0.12 Alpine, MongoDB 7.0, SQL Server 2022-latest, and KurrentDB 25.1.3.
Each test owns unique stream names. No application databases are used. Fixture
startup is shared, so a failed backend startup blocks the integration tests;
that is infrastructure failure, not evidence of snapshot defects.

| Tests | Coverage |
| --- | --- |
| `SnapshotStoreTests` | Four external stores: missing/read/write/delete, Unicode and decimal fidelity, revisions above Int32, replacement, idempotency, null rejection, delayed older writer |
| `ReplayTests` | Full replay versus snapshot and tail, state and revision, aggregate loading, subsequent append, all three strategies, snapshot deletion/rebuild, latest snapshot, 499/500/501-event boundaries and multiple pages |
| `CommandServiceTests` | Functional and aggregate services, repeated snapshot thresholds, no-op commands, all stores and strategies, failure after commit and duplicate retry, missing configuration, snapshot-only revision, mid-batch snapshot |
| `RedisConsistencyTests` | Real Redis replacement precisely between scalar reads, resulting duplicate replay, no-replacement control |
| `GeneratorTests` | Compilable generic/non-generic declarations, strategy configuration, multiple snapshot contracts |
| `FailureBoundaryTests` | Pre-cancelled writes on four stores, corrupt/unknown Redis payloads and deletion recovery, deleted Kurrent stream with retained snapshot, PostgreSQL deletion limitation, synchronized optimistic concurrency for both services |

`Contract` asserts expected behavior. `KnownDefect` asserts desirable behavior
that is broken at the reviewed head; failures must still be inspected, never
assumed correct merely because of the label. `Reproduction` characterizes an
undesirable behavior: a passing test confirms it occurs.

## Verified evidence (2026-10-08)

The original 76-case suite ran against all five backends:
**59 passed, 17 failed**, in `snapshots-baseline-docker.trx`.
The final expanded suite ran **95 cases: 68 passed, 27 failed, none skipped**,
in `snapshots-final.trx`. Production code was unchanged throughout. See `RESULTS.md` for report summaries
and individual failed assertions from the final expanded suite.

| Finding | Observed result |
| --- | --- |
| Backward pagination | Six Kurrent cases exhaust the four-read budget. Cursors start at `9223372036854775807`, then `9223372036854775307`, rather than the actual previous event revision. Includes exactly 500 events without a snapshot. |
| Redis mixed read | The controlled replacement executes once. Expected 11 applied events and balance 5.00; actual 12 and 6.25. |
| Snapshot failure after commit | Both services report failure after persisting deposit 10; retry produces balance 20. These two reproduction tests pass. |
| Delayed older write | All four stores replace revision 110 with revision 100. Monotonic writes are a proposed stronger contract, not a guarantee documented by `ISnapshotStore`. |
| Generic attribute | Both generic registration tests fail; non-generic and multiple-contract controls pass after compiler-error checks. |
| PostgreSQL backward reads | Both SameStream and SeparateStream overflow converting the End sentinel to the provider's integer parameter. SeparateStore replay passes. |
| Snapshot-only command | Both service styles use the no-op append's revision -1. PostgreSQL and SQL Server reject it through revision constraints; Redis and MongoDB persist -1 instead of the true revision 2. |
| Mid-batch explicit snapshot | Command returns balance 15, but loading returns 10 because the snapshot claims the final batch revision. |
| Cancellation | Redis persists a snapshot despite a pre-cancelled token; PostgreSQL, MongoDB, and SQL Server reject the write without persisting it. This covers pre-cancellation, not cancellation during I/O. |
| Unreadable Redis payload | Invalid JSON and an unknown contract both block loading despite retained domain history. Explicit snapshot deletion restores replay. |
| Deleted domain stream | Kurrent deletion leaves the external snapshot behind. Loading with `failIfNotFound: false` restores stale state and revision; true throws. Removing the snapshot restores empty-state behavior. PostgreSQL `DeleteStream` is unimplemented. |
| PostgreSQL concurrent commands | Both commands load revision 0 and report success, but only one new event is durable. Functional: full replay balance 21, snapshot replay 11; aggregate: full replay 11, snapshot replay 21. Kurrent controls correctly reject one append and preserve snapshot state. This exposes a provider issue; its origin is not established as part of PR #479. |

## Harness corrections and limits

- `snapshots-initial.trx` is the pre-reboot infrastructure/harness report.
  `snapshots-baseline.trx` is the post-reboot sandbox/Docker-access failure.
  Neither is an implementation baseline.
- `snapshots-expanded.trx` is an intermediate 93-case run (65 passed, 28 failed).
  Its PostgreSQL deletion case could not reach snapshot assertions because
  deletion is unimplemented. It also attempted simultaneous creation of a new
  PostgreSQL stream and observed a raw `PostgresException` rather than the
  expected concurrency exception. The final harness explicitly characterizes
  unsupported deletion and seeds an existing stream before competing commands;
  it does not broaden the expected exception or claim the new-stream race fixed.
- `snapshots-validated.trx` preserves the intermediate existing-stream run
  (93 cases, 66 passed, 27 failed). Both PostgreSQL writers reported success.
  The final suite adds expected-revision diagnostics and Kurrent controls.
  The gate ensures both commands load revision 0, but database scheduling can
  affect whether the PostgreSQL race reproduces in a subsequent run.
- The generator probe excludes the generator assembly from dynamic compilation
  references because it links a second copy of `SnapshotStorageStrategy`.
- The Redis gate targets the reviewed scalar-read implementation. Before using
  it to validate an atomic-read fix, adapt the gate to the new command shape;
  do not mistake an unexecuted gate for proof that an atomic fix is broken.
- The suite retains real database reads/writes; gates only control scheduling.
  Backward reads are bounded so the cursor bug cannot hang a test indefinitely.
- SQL Server and Mongo image tags can move. Exact image identifiers from this
  session are recorded in `RESULTS.md` when available; no cross-version claim is made.
- Mid-I/O cancellation, customized serializers, historical contract migration,
  and companion-stream write/truncate failure injection remain future coverage.
  The usage guide's standalone HTTP application has not been compiled or run.

The earlier review and usage guide live in sibling repositories; see the root
`SNAPSHOT-TEST-PLAN.md` for their paths and preservation requirements.
