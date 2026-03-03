# SQLite Event Store Design

## Purpose

Add an SQLite-based event store implementation for embedded/local apps (desktop, mobile, CLI) that need a local event store without external database dependencies. Full feature parity with PostgreSQL and SQL Server implementations.

## Architecture

Extends `SqlEventStoreBase<SqliteConnection, SqliteTransaction>` from `Eventuous.Sql.Base`, following the same patterns as `Eventuous.Postgresql` and `Eventuous.SqlServer`.

### Components

- **SqliteStore** — Event store (read, append, truncate, stream exists)
- **SqliteCheckpointStore** — Checkpoint persistence
- **SqliteStreamSubscription** / **SqliteAllStreamSubscription** — Polling subscriptions
- **SqliteProjector** — Base class for read model projections
- **Schema** — SQL strings and schema creation from embedded scripts
- **SchemaInitializer** — IHostedService for auto-initialization
- **ConnectionFactory** — Connection management with WAL mode
- **RegistrationExtensions** — DI registration (`AddEventuousSqlite`)

### Directory Structure

```
src/Sqlite/
├── src/Eventuous.Sqlite/
│   ├── Eventuous.Sqlite.csproj
│   ├── SqliteStore.cs
│   ├── Schema.cs
│   ├── SchemaInitializer.cs
│   ├── ConnectionFactory.cs
│   ├── Extensions/
│   │   ├── SqliteExtensions.cs
│   │   └── RegistrationExtensions.cs
│   ├── Projections/
│   │   └── SqliteProjector.cs
│   ├── Subscriptions/
│   │   ├── SqliteSubscriptionBase.cs
│   │   ├── SqliteStreamSubscription.cs
│   │   ├── SqliteAllStreamSubscription.cs
│   │   └── SqliteCheckpointStore.cs
│   └── Scripts/
│       ├── 1_Schema.sql
│       ├── 2_AppendEvents.sql
│       ├── 3_CheckStream.sql
│       ├── 4_ReadAllForwards.sql
│       ├── 5_ReadStreamBackwards.sql
│       ├── 6_ReadStreamForwards.sql
│       ├── 7_ReadStreamSub.sql
│       └── 8_TruncateStream.sql
└── test/Eventuous.Tests.Sqlite/
    ├── Eventuous.Tests.Sqlite.csproj
    ├── Fixtures/SqliteFixture.cs
    ├── Store/StoreTests.cs
    ├── Subscriptions/
    │   ├── SubscribeTests.cs
    │   └── SubscriptionFixture.cs
    ├── Projections/ProjectorTests.cs
    ├── Registrations/RegistrationTests.cs
    └── Metrics/MetricsTests.cs
```

## SQLite-Specific Decisions

1. **No schema support** — Table name prefix (e.g., `eventuous_streams`) instead of SQL schema namespace
2. **No stored procedures** — Inline SQL statements; append logic runs as multi-statement block in transaction from C#
3. **WAL mode** — Enabled by default for concurrent read access
4. **No TVPs** — Events inserted row-by-row within transaction
5. **Global position** — `INTEGER PRIMARY KEY AUTOINCREMENT` for gap-free sequence
6. **Error detection** — SqliteException error code 19 (SQLITE_CONSTRAINT) for conflicts
7. **Provider** — `Microsoft.Data.Sqlite`
8. **Namespace** — `Eventuous.Sqlite`

## Schema

```sql
CREATE TABLE IF NOT EXISTS __schema___streams (
    stream_id   INTEGER PRIMARY KEY AUTOINCREMENT,
    stream_name TEXT    NOT NULL UNIQUE,
    version     INTEGER NOT NULL DEFAULT(-1),
    CHECK(version >= -1)
);

CREATE TABLE IF NOT EXISTS __schema___messages (
    global_position INTEGER PRIMARY KEY AUTOINCREMENT,
    message_id      TEXT    NOT NULL,
    message_type    TEXT    NOT NULL,
    stream_id       INTEGER NOT NULL REFERENCES __schema___streams(stream_id),
    stream_position INTEGER NOT NULL,
    json_data       TEXT    NOT NULL,
    json_metadata   TEXT,
    created         TEXT    NOT NULL,
    UNIQUE(stream_id, stream_position),
    UNIQUE(stream_id, message_id),
    CHECK(stream_position >= 0)
);

CREATE TABLE IF NOT EXISTS __schema___checkpoints (
    id       TEXT    PRIMARY KEY,
    position INTEGER NULL
);
```
