# Snapshot test evidence — 2026-10-08

Implementation: `a06a726ea6eef2186d4cc077220e081647c772eb`; no production modifications.

Environment: Ubuntu 24.04.5 LTS, .NET runtime 10.0.12, TUnit 0.77.3, Docker 28.1.1. Build passed with 0 errors; existing dependency audit warnings remain.

## Reports

The reports below are archived in [evidence/](evidence/).

| Report | Total | Passed | Failed | SHA-256 |
| --- | ---: | ---: | ---: | --- |
| [snapshots-baseline-docker.trx](evidence/snapshots-baseline-docker.trx) | 76 | 59 | 17 | `ab244e8e633bcb18646d53b8554d64e3ef9479c1c1aea710f21160306b65a984` |
| [snapshots-expanded.trx](evidence/snapshots-expanded.trx) | 93 | 65 | 28 | `218fde33a53f28a1a09c5eaca2e21a1a930c4ae3cfef40d3e8878f9430d192e7` |
| [snapshots-validated.trx](evidence/snapshots-validated.trx) | 93 | 66 | 27 | `02bdefb89d2b9aa67f310f184a55f46d7194f0f1625638a7b91ccfa2dbc6a1e5` |
| [snapshots-final.trx](evidence/snapshots-final.trx) | 95 | 68 | 27 | `c7bf7a2ad900acb8f17fda99bbf5edfe3c1cb2f874b05bfd5ad6e47c5edc7ad2` |
| [snapshots-reproductions.trx](evidence/snapshots-reproductions.trx) | 6 | 6 | 0 | `d63ca6c8b8dcdae13aa4d09d5fd178d8403962316fb5fb0030409224fe0cb386` |

## Final suite

| Class | Passed | Failed |
| --- | ---: | ---: |
| CommandServiceTests | 15 | 9 |
| FailureBoundaryTests | 9 | 3 |
| GeneratorTests | 2 | 2 |
| RedisConsistencyTests | 1 | 1 |
| ReplayTests | 17 | 8 |
| SnapshotStoreTests | 24 | 4 |

| Category | Passed | Failed |
| --- | ---: | ---: |
| Contract | 62 | 5 |
| KnownDefect | 0 | 22 |
| Reproduction | 6 | 0 |

`Reproduction` passing establishes undesirable behavior. The intermediate reports include harness limitations described in README.md. Database concurrency outcomes depend on scheduling; recorded failures are observations, not a promise that every run reproduces them.

## Failed assertions in final suite

### Cancelled_write_must_not_create_snapshot(Redis)

```text
ShouldAssertException: error
    should not be null but was
```

```text
Redis: pre-cancelled write exception=none, persisted=True
```

### Concurrent_commands_only_snapshot_the_successful_append(Postgres, False)

```text
ShouldAssertException: results.Count(r => r.Success)
    should be
1
    but was
2
```

```text
Postgres, aggregate=False: both expected revision 0; successes=2, durable events=2, full balance=21, snapshot revision=1, loaded balance=11
```

### Concurrent_commands_only_snapshot_the_successful_append(Postgres, True)

```text
ShouldAssertException: results.Count(r => r.Success)
    should be
1
    but was
2
```

```text
Postgres, aggregate=True: both expected revision 0; successes=2, durable events=2, full balance=11, snapshot revision=1, loaded balance=21
```

### Concurrent_replacement_must_not_mix_revision_and_payload_or_double_replay

```text
ShouldAssertException: actual.State.Applied
    should be
11
    but was
12

Additional Info:
    Snapshot revision 9 must not be paired with revision-10 state, replaying event 10 twice.
```

```text
Redis interleaving: expected applied=11, balance=5.00; actual applied=12, balance=6.25
```

### Delayed_older_writer_must_not_replace_newer_snapshot(Mongo)

```text
ShouldAssertException: loaded.Revision
    should be
110L
    but was
100L

Additional Info:
    Snapshot writes should be monotonic in represented stream revision.
```

```text
Mongo: newer revision 110 committed before delayed revision 100; stored revision=100
```

### Delayed_older_writer_must_not_replace_newer_snapshot(Postgres)

```text
ShouldAssertException: loaded.Revision
    should be
110L
    but was
100L

Additional Info:
    Snapshot writes should be monotonic in represented stream revision.
```

```text
Postgres: newer revision 110 committed before delayed revision 100; stored revision=100
```

### Delayed_older_writer_must_not_replace_newer_snapshot(Redis)

```text
ShouldAssertException: loaded.Revision
    should be
110L
    but was
100L

Additional Info:
    Snapshot writes should be monotonic in represented stream revision.
```

```text
Redis: newer revision 110 committed before delayed revision 100; stored revision=100
```

### Delayed_older_writer_must_not_replace_newer_snapshot(SqlServer)

```text
ShouldAssertException: loaded.Revision
    should be
110L
    but was
100L

Additional Info:
    Snapshot writes should be monotonic in represented stream revision.
```

```text
SqlServer: newer revision 110 committed before delayed revision 100; stored revision=100
```

### Explicit_snapshot_inside_batch_must_not_skip_following_domain_event

```text
ShouldAssertException: loaded.State.Balance
    should be
15m
    but was
10m

Additional Info:
    The snapshot precedes the final event and must not claim to include it.
```

```text
Mid-batch snapshot: command result balance=15; reloaded balance=10
```

### Generic_attribute_constructor_strategy_must_be_respected

```text
ShouldAssertException: generated
    should contain (case insensitive comparison)
"SnapshotTypeMap.Register(typeof(global::Example.MyState), typeof(global::Example.MySnapshot), SnapshotStorageStrategy.SeparateStream)"
    but was actually
"// <auto-generated/>
#pragma warning disable CS8019
#nullable enable

using System;
using System.Run..."
```

### Generic_attribute_used_by_banking_sample_must_register_snapshot_type

```text
ShouldAssertException: generated
    should contain (case insensitive comparison)
"SnapshotTypeMap.Register(typeof(global::Example.MyState), typeof(global::Example.MySnapshot), SnapshotStorageStrategy.SeparateStore)"
    but was actually
"// <auto-generated/>
#pragma warning disable CS8019
#nullable enable

using System;
using System.Run..."
```

### No_snapshot_must_fall_back_to_complete_multi_page_replay(1001)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### No_snapshot_must_fall_back_to_complete_multi_page_replay(500)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### No_snapshot_must_fall_back_to_complete_multi_page_replay(501)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### Postgres_supports_snapshot_backward_reads(SameStream)

```text
ReadFromStreamException: Unable to read events from SnapshotTest-d00ba95e2ea243caa419f84cf461463e: Arithmetic operation resulted in an overflow.
```

### Postgres_supports_snapshot_backward_reads(SeparateStream)

```text
ReadFromStreamException: Unable to read events from SnapshotTestSnapshot-c2c1144dde5840e0bc44f8dffdc20efe: Arithmetic operation resulted in an overflow.
```

### Same_stream_snapshot_beyond_first_page_must_replay_without_repeating_pages(1000)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### Same_stream_snapshot_beyond_first_page_must_replay_without_repeating_pages(500)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### Same_stream_snapshot_beyond_first_page_must_replay_without_repeating_pages(501)

```text
InvalidOperationException: Backward read budget exceeded. Cursors: 9223372036854775807, 9223372036854775307, 9223372036854774807, 9223372036854774307, 9223372036854773807
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Mongo, False)

```text
ShouldAssertException: snapshot.Revision
    should be
2L
    but was
-1L

Additional Info:
    An empty domain append must not reset the represented revision to AppendEventsResult.NoOp's value.
```

```text
Mongo, aggregate=False: snapshot-only command success=True, error=
Snapshot-only command: snapshot revision=-1; actual domain revision=2
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Mongo, True)

```text
ShouldAssertException: snapshot.Revision
    should be
2L
    but was
-1L

Additional Info:
    An empty domain append must not reset the represented revision to AppendEventsResult.NoOp's value.
```

```text
Mongo, aggregate=True: snapshot-only command success=True, error=
Snapshot-only command: snapshot revision=-1; actual domain revision=2
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Postgres, False)

```text
PostgresException: 23514: new row for relation "snapshots" violates check constraint "ck_revision_gte_zero"

DETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information.
```

```text
Postgres, aggregate=False: snapshot-only command success=False, error=23514: new row for relation "snapshots" violates check constraint "ck_revision_gte_zero"
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Postgres, True)

```text
PostgresException: 23514: new row for relation "snapshots" violates check constraint "ck_revision_gte_zero"

DETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information.
```

```text
Postgres, aggregate=True: snapshot-only command success=False, error=23514: new row for relation "snapshots" violates check constraint "ck_revision_gte_zero"
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Redis, False)

```text
ShouldAssertException: snapshot.Revision
    should be
2L
    but was
-1L

Additional Info:
    An empty domain append must not reset the represented revision to AppendEventsResult.NoOp's value.
```

```text
Redis, aggregate=False: snapshot-only command success=True, error=
Snapshot-only command: snapshot revision=-1; actual domain revision=2
```

### Snapshot_only_command_must_preserve_existing_stream_revision(Redis, True)

```text
ShouldAssertException: snapshot.Revision
    should be
2L
    but was
-1L

Additional Info:
    An empty domain append must not reset the represented revision to AppendEventsResult.NoOp's value.
```

```text
Redis, aggregate=True: snapshot-only command success=True, error=
Snapshot-only command: snapshot revision=-1; actual domain revision=2
```

### Snapshot_only_command_must_preserve_existing_stream_revision(SqlServer, False)

```text
SqlException: The MERGE statement conflicted with the CHECK constraint "CK_Snapshots_RevisionGteZero". The conflict occurred in database "master", table "snapshot_tests.snapshots", column 'revision'.
The statement has been terminated.
```

```text
SqlServer, aggregate=False: snapshot-only command success=False, error=The MERGE statement conflicted with the CHECK constraint "CK_Snapshots_RevisionGteZero". The conflict occurred in database "master", table "snapshot_tests.snapshots", column 'revision'.
```

### Snapshot_only_command_must_preserve_existing_stream_revision(SqlServer, True)

```text
SqlException: The MERGE statement conflicted with the CHECK constraint "CK_Snapshots_RevisionGteZero". The conflict occurred in database "master", table "snapshot_tests.snapshots", column 'revision'.
The statement has been terminated.
```

```text
SqlServer, aggregate=True: snapshot-only command success=False, error=The MERGE statement conflicted with the CHECK constraint "CK_Snapshots_RevisionGteZero". The conflict occurred in database "master", table "snapshot_tests.snapshots", column 'revision'.
```

## Concurrency controls and passing reproductions

| Test | Outcome |
| --- | --- |
| Concurrent_commands_only_snapshot_the_successful_append(Kurrent, False) | Passed |
| Concurrent_commands_only_snapshot_the_successful_append(Kurrent, True) | Passed |
| Concurrent_commands_only_snapshot_the_successful_append(Postgres, False) | Failed |
| Concurrent_commands_only_snapshot_the_successful_append(Postgres, True) | Failed |
| Deleted_domain_stream_with_leftover_snapshot_can_restore_stale_state(Kurrent) | Passed |
| Postgres_domain_stream_deletion_is_not_implemented | Passed |
| Snapshot_write_failure_reports_error_after_commit_and_retry_duplicates_business_event(False) | Passed |
| Snapshot_write_failure_reports_error_after_commit_and_retry_duplicates_business_event(True) | Passed |
| Unreadable_redis_snapshot_blocks_replay_until_deleted(False) | Passed |
| Unreadable_redis_snapshot_blocks_replay_until_deleted(True) | Passed |

## Docker image identities

| Image | Local image ID | Registry digest |
| --- | --- | --- |
| postgres:14 | e8316dc7ad10 | sha256:c2427de38f998489d36de7ca3553db2134872c400f2b08be4b824e5c50e4d619 |
| redis:7.0.12-alpine | 6c09b0364aa8 | sha256:32dcb8aedb557eb0d3463537f5fc3c765d1688d4940d487c1f0c3752f2c3916c |
| mongo:7.0 | ac1502203bff | sha256:1f995ad6fdb93244a1addab1b58f934a0bc2f5643c38e02f5e9d7f0c7d227a7b |
| mcr.microsoft.com/mssql/server:2022-latest | 3c94bf005911 | sha256:b1395aa51b4ec39981883560f1379ea9eba2a1c0719bf8e6477902769316bb79 |
| kurrentplatform/kurrentdb:25.1.3 | b507b8f0e91e | sha256:6f0ee73ecefe0ea999858306e0171bbddfff45123a84e0429cf197a08cae04d5 |
