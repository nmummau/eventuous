# SQLite Event Store Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an SQLite-based event store implementation for embedded/local apps, with full feature parity (event store, subscriptions, projections, checkpoint store) matching PostgreSQL and SQL Server implementations.

**Architecture:** Extends `SqlEventStoreBase<SqliteConnection, SqliteTransaction>` from `Eventuous.Sql.Base`. Since SQLite has no stored procedures, all SQL is inline text commands. Since SQLite has no schema namespaces, a table name prefix (e.g. `eventuous_`) replaces the `__schema__` placeholder. Uses `Microsoft.Data.Sqlite` ADO.NET provider.

**Tech Stack:** Microsoft.Data.Sqlite, Eventuous.Sql.Base, TUnit (tests)

**Important SQLite quirks:**
- `SqliteDataReader.GetGuid()` expects TEXT column with guid format — works with `Microsoft.Data.Sqlite`
- No stored procedures — all SQL is inline text
- No table-valued parameters — events inserted in a loop within transaction
- `INTEGER PRIMARY KEY AUTOINCREMENT` for gap-free global position
- No schema namespaces — use table prefix `__schema___` (double underscore, then schema name, then underscore)
- `RETURNING` clause supported since SQLite 3.35 (2021), safe to use
- `datetime('now')` for UTC timestamps, stored as TEXT in ISO8601
- Error code 19 = SQLITE_CONSTRAINT for conflicts

---

### Task 1: Add Microsoft.Data.Sqlite to Directory.Packages.props

**Files:**
- Modify: `Directory.Packages.props`

**Step 1: Add package version**

Add to the main `<ItemGroup>` (near line 51, after `Microsoft.Data.SqlClient`):

```xml
<PackageVersion Include="Microsoft.Data.Sqlite" Version="9.0.6" />
```

**Step 2: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: add Microsoft.Data.Sqlite package version"
```

---

### Task 2: Create Eventuous.Sqlite project and Schema

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/Eventuous.Sqlite.csproj`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Schema.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Scripts/1_Schema.sql`

**Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
        <ProjectReference Include="$(CoreRoot)\Eventuous.Subscriptions\Eventuous.Subscriptions.csproj"/>
        <ProjectReference Include="$(CoreRoot)\Eventuous.Persistence\Eventuous.Persistence.csproj"/>
        <ProjectReference Include="$(SrcRoot)\Relational\src\Eventuous.Sql.Base\Eventuous.Sql.Base.csproj"/>
        <ProjectReference Include="$(CoreRoot)\Eventuous.Producers\Eventuous.Producers.csproj"/>
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Microsoft.Data.Sqlite"/>
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions"/>
    </ItemGroup>
    <ItemGroup>
        <EmbeddedResource Include="Scripts\1_Schema.sql"/>
    </ItemGroup>
    <ItemGroup>
        <Compile Include="$(CoreRoot)\Eventuous.Shared\Tools\TaskExtensions.cs">
            <Link>Tools\TaskExtensions.cs</Link>
        </Compile>
        <Compile Include="$(CoreRoot)\Eventuous.Shared\Tools\Ensure.cs">
            <Link>Tools\Ensure.cs</Link>
        </Compile>
    </ItemGroup>
    <ItemGroup>
        <InternalsVisibleTo Include="Eventuous.Tests.Sqlite"/>
    </ItemGroup>
    <ItemGroup>
        <Using Include="Microsoft.Data.Sqlite"/>
        <Using Include="Eventuous.Tools"/>
    </ItemGroup>
</Project>
```

**Step 2: Create 1_Schema.sql**

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

CREATE INDEX IF NOT EXISTS __schema___idx_messages_stream ON __schema___messages(stream_id);

CREATE TABLE IF NOT EXISTS __schema___checkpoints (
    id       TEXT    PRIMARY KEY,
    position INTEGER NULL
);
```

**Step 3: Create Schema.cs**

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Eventuous.Sqlite;

public class Schema(string schema = Schema.DefaultSchema) {
    public const string DefaultSchema = "eventuous";

    public string StreamsTable     => $"{schema}_streams";
    public string MessagesTable    => $"{schema}_messages";
    public string CheckpointsTable => $"{schema}_checkpoints";

    public string StreamExists        => $"SELECT CASE WHEN EXISTS(SELECT 1 FROM {schema}_streams WHERE stream_name = @name) THEN 1 ELSE 0 END";
    public string GetCheckpointSql    => $"SELECT position FROM {schema}_checkpoints WHERE id = @checkpointId";
    public string AddCheckpointSql    => $"INSERT INTO {schema}_checkpoints (id) VALUES (@checkpointId)";
    public string UpdateCheckpointSql => $"UPDATE {schema}_checkpoints SET position = @position WHERE id = @checkpointId";

    static readonly Assembly Assembly = typeof(Schema).Assembly;

    public string SchemaName => schema;

    [PublicAPI]
    public async Task CreateSchema(string connectionString, ILogger<Schema>? log, CancellationToken cancellationToken) {
        log?.LogInformation("Creating schema with prefix {Schema}", schema);

        var names = Assembly.GetManifestResourceNames()
            .Where(x => x.EndsWith(".sql"))
            .OrderBy(x => x);

        await using var connection = await ConnectionFactory.GetConnection(connectionString, cancellationToken).NoContext();
        await using var transaction = connection.BeginTransaction();

        try {
            foreach (var name in names) {
                log?.LogInformation("Executing {Script}", name);
                await using var stream = Assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream!);
                var script = await reader.ReadToEndAsync(cancellationToken).NoContext();
                var cmdScript = script.Replace("__schema__", schema);

                await using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = cmdScript;
                await cmd.ExecuteNonQueryAsync(cancellationToken).NoContext();
            }

            await transaction.CommitAsync(cancellationToken).NoContext();
        } catch (Exception e) {
            log?.LogCritical(e, "Unable to initialize the database schema");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        log?.LogInformation("Database schema initialized");
    }
}
```

**Step 4: Commit**

```bash
git add src/Sqlite/
git commit -m "feat: add Sqlite project with schema and SQL scripts"
```

---

### Task 3: Create ConnectionFactory and SqliteExtensions

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/ConnectionFactory.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Extensions/SqliteExtensions.cs`

**Step 1: Create ConnectionFactory.cs**

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.Sqlite;

delegate Task<SqliteConnection> GetSqliteConnection(CancellationToken cancellationToken);

public static class ConnectionFactory {
    public static async Task<SqliteConnection> GetConnection(string connectionString, CancellationToken cancellationToken) {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).NoContext();

        // Enable WAL mode for better concurrent read performance
        await using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync(cancellationToken).NoContext();

        return connection;
    }
}
```

**Step 2: Create SqliteExtensions.cs**

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.Sqlite.Extensions;

static class SqliteExtensions {
    extension(SqliteCommand command) {
        internal SqliteCommand Add(string parameterName, object? value) {
            command.Parameters.AddWithValue(parameterName, value ?? DBNull.Value);
            return command;
        }
    }

    extension(SqliteConnection connection) {
        internal SqliteCommand GetTextCommand(string sql, SqliteTransaction? transaction = null) {
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            if (transaction != null) cmd.Transaction = transaction;
            return cmd;
        }
    }
}
```

**Step 3: Commit**

```bash
git add src/Sqlite/
git commit -m "feat: add Sqlite connection factory and extensions"
```

---

### Task 4: Create SqliteStore (event store implementation)

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/SqliteStore.cs`

**Step 1: Create SqliteStore.cs**

The key difference from SQL Server/Postgres: since SQLite has no stored procedures, `GetAppendCommand` cannot use a single command. We override `AppendEvents` directly to implement the check-stream + insert loop + update version logic in C# with a transaction.

However, looking at the base class, `GetAppendCommand` returns a `DbCommand` that is executed via `ExecuteReaderAsync` and expects to return `(version, global_position)`. For SQLite we need to handle the append differently since we can't do all the logic in a single SQL command.

The cleanest approach: override `AppendEvents` entirely in `SqliteStore` (the base class method is virtual/overridable since it implements the interface). But looking at the base class, `AppendEvents` is not virtual — it's a direct `IEventStore` implementation.

So we'll make `GetAppendCommand` work by using a multi-statement SQL that:
1. Inserts/gets the stream
2. Validates version
3. Returns a reader with (version, global_position)

And we'll insert events one-by-one before calling the append command. Actually, looking more carefully at the base class flow:

```csharp
await using var cmd = GetAppendCommand(connection, transaction, stream, expectedVersion, persistedEvents);
await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
await reader.ReadAsync(cancellationToken);
result = new((ulong)reader.GetInt64(1), reader.GetInt32(0));
```

The command needs to: append events and return (new_version INT, global_position BIGINT). For SQLite, we can use a multi-statement command that does everything inline. SQLite supports multiple statements in one `CommandText`, but `ExecuteReaderAsync` only returns results from the last SELECT.

Approach: The `GetAppendCommand` will build a command text that:
1. Checks/creates the stream (INSERT OR IGNORE + SELECT to get stream_id + validate version)
2. Inserts each event as a separate INSERT with parameterized values
3. Updates the stream version
4. SELECTs the new version and max global_position

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Data.Common;
using System.Text;
using Eventuous.Sql.Base;
using Eventuous.Sqlite.Extensions;

namespace Eventuous.Sqlite;

public record SqliteStoreOptions {
    public string ConnectionString   { get; init; } = "Data Source=eventuous.db";
    public string Schema             { get; init; } = Schema.DefaultSchema;
    public bool   InitializeDatabase { get; init; }
}

public class SqliteStore : SqlEventStoreBase<SqliteConnection, SqliteTransaction> {
    readonly GetSqliteConnection _getConnection;

    public Schema Schema { get; }

    public SqliteStore(SqliteStoreOptions options, IEventSerializer? serializer = null, IMetadataSerializer? metaSerializer = null)
        : base(serializer, metaSerializer) {
        var connectionString = Ensure.NotEmptyString(options.ConnectionString);
        _getConnection = ct => ConnectionFactory.GetConnection(connectionString, ct);
        Schema = new(options.Schema);
    }

    protected override async ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken)
        => await _getConnection(cancellationToken).NoContext();

    protected override DbCommand GetReadCommand(SqliteConnection connection, StreamName stream, StreamReadPosition start, int count) {
        var sql = $"""
            SELECT m.message_id, m.message_type, m.stream_position, m.global_position, m.json_data, m.json_metadata, m.created
            FROM {Schema.MessagesTable} m
            JOIN {Schema.StreamsTable} s ON m.stream_id = s.stream_id
            WHERE s.stream_name = @stream_name
            AND m.stream_position >= @from_position
            ORDER BY m.stream_position
            LIMIT @count
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@from_position", start.Value)
            .Add("@count", count);
    }

    protected override DbCommand GetReadBackwardsCommand(SqliteConnection connection, StreamName stream, StreamReadPosition start, int count) {
        // If start position exceeds current version, clamp it to the current version
        var sql = $"""
            SELECT m.message_id, m.message_type, m.stream_position, m.global_position, m.json_data, m.json_metadata, m.created
            FROM {Schema.MessagesTable} m
            JOIN {Schema.StreamsTable} s ON m.stream_id = s.stream_id
            WHERE s.stream_name = @stream_name
            AND m.stream_position <= MIN(@from_position, s.version)
            ORDER BY m.stream_position DESC
            LIMIT @count
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@from_position", start.Value)
            .Add("@count", count);
    }

    protected override bool IsStreamNotFound(Exception exception) => exception is SqliteException { SqliteErrorCode: 1 } e && e.Message.Contains("StreamNotFound");

    protected override DbCommand GetAppendCommand(
            SqliteConnection      connection,
            SqliteTransaction     transaction,
            StreamName            stream,
            ExpectedStreamVersion expectedVersion,
            NewPersistedEvent[]   events
        ) {
        var sb = new StringBuilder();
        var cmd = connection.GetTextCommand("", transaction);

        // Step 1: Get or create the stream
        sb.AppendLine($"""
            INSERT INTO {Schema.StreamsTable} (stream_name, version)
            VALUES (@stream_name, -1)
            ON CONFLICT(stream_name) DO NOTHING;
            """);

        // Step 2: Get stream_id and current version, validate expected version
        // We use a trick: if validation fails, we raise an error via a subquery that references a non-existent table
        sb.AppendLine($"""
            SELECT stream_id, version INTO @sid, @ver
            FROM {Schema.StreamsTable}
            WHERE stream_name = @stream_name;
            """);

        // Actually, SQLite doesn't support SELECT INTO variables. We need a different approach.
        // We'll build a CTE-based approach or use the results in application code.
        // Let's use a simpler approach: do the stream check and inserts all in one go.

        // Reset and use a different strategy: build all SQL inline
        sb.Clear();

        // The approach: use a single multi-statement command
        // 1. Ensure stream exists
        // 2. Validate version
        // 3. Insert events
        // 4. Update stream version
        // 5. Return new version and last global position

        var streamName = stream.ToString();
        var expected = expectedVersion.Value;
        var created = DateTime.UtcNow.ToString("O");

        // Use parameterized approach
        cmd.Parameters.AddWithValue("@stream_name", streamName);
        cmd.Parameters.AddWithValue("@expected_version", expected);
        cmd.Parameters.AddWithValue("@created", created);

        // For SQLite, we need to handle this differently since we can't use variables.
        // We'll handle the check-stream and append logic by reading stream info first,
        // then building the insert statements.
        // Actually, the simplest correct approach is: since SQLite doesn't support
        // stored procs, we implement the full append logic by overriding AppendEvents.
        // But the base class's AppendEvents is not virtual.
        //
        // Alternative: use a complex SQL with CTEs and RAISE().
        // SQLite supports RAISE(ABORT, 'message') in triggers but not in plain SQL.
        //
        // Best approach for SQLite: Build a single SQL command that does everything,
        // using subqueries and CASE expressions, and raise errors via constraint violations.

        sb.Clear();
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@stream_name", streamName);
        cmd.Parameters.AddWithValue("@expected_version", expected);
        cmd.Parameters.AddWithValue("@created", created);

        // Insert stream if not exists
        sb.AppendLine($"INSERT OR IGNORE INTO {Schema.StreamsTable} (stream_name, version) VALUES (@stream_name, -1);");

        // For version checking, we need to handle three cases:
        // -2 = Any (skip check), -1 = NoStream (version must be -1), 0+ = specific version
        // If NoStream and stream already has events, or if specific version doesn't match, we need to fail.
        // We'll use a SELECT that returns stream_id only if version matches, and cause an error otherwise.

        // The approach: insert events with stream_position computed from current version.
        // If there's a conflict on (stream_id, stream_position), it means wrong version.
        // This mirrors how PostgreSQL handles it.

        var eventCount = events.Length;
        for (var i = 0; i < events.Length; i++) {
            var evt = events[i];
            var idxStr = i.ToString();

            cmd.Parameters.AddWithValue($"@msg_id_{idxStr}", evt.MessageId.ToString());
            cmd.Parameters.AddWithValue($"@msg_type_{idxStr}", evt.MessageType);
            cmd.Parameters.AddWithValue($"@json_data_{idxStr}", evt.JsonData);
            cmd.Parameters.AddWithValue($"@json_metadata_{idxStr}", (object?)evt.JsonMetadata ?? DBNull.Value);

            sb.AppendLine($"""
                INSERT INTO {Schema.MessagesTable} (message_id, message_type, stream_id, stream_position, json_data, json_metadata, created)
                SELECT s.stream_id, @msg_type_{idxStr},
                    (SELECT stream_id FROM {Schema.StreamsTable} WHERE stream_name = @stream_name),
                    (SELECT version FROM {Schema.StreamsTable} WHERE stream_name = @stream_name) + {i + 1},
                    @json_data_{idxStr}, @json_metadata_{idxStr}, @created
                FROM {Schema.StreamsTable} s
                WHERE s.stream_name = @stream_name;
                """);
        }

        // Hmm, this is getting complex. Let me reconsider the approach entirely.
        // The cleanest way is to acknowledge that SQLite can't do this in one command
        // and instead we need to split the work. Since the base class controls the flow,
        // but the command is built here, we should make the command work.

        // Simplest correct approach: Use a TEMPORARY TABLE or just do it step by step.
        // Actually, the simplest approach for SQLite is:
        // - The command text contains multiple statements
        // - The last statement is a SELECT returning (new_version, global_position)
        // - SQLite allows multiple statements; ExecuteReader returns results from the last SELECT

        sb.Clear();
        cmd.Parameters.Clear();

        cmd.Parameters.AddWithValue("@stream_name", streamName);
        cmd.Parameters.AddWithValue("@expected_version", expected);
        cmd.Parameters.AddWithValue("@created", created);

        // 1. Ensure stream row exists
        sb.AppendLine($"INSERT OR IGNORE INTO {Schema.StreamsTable} (stream_name, version) VALUES (@stream_name, -1);");

        // 2. Version check: if expected_version is not -2 (Any), validate
        // For NoStream (-1): current version must be -1 (just created or empty)
        // For specific version (0+): current version must match
        // We do this by trying to insert events at (current_version + 1..N).
        // If version doesn't match, UNIQUE(stream_id, stream_position) will fail.
        // But we also need to handle the case where expected is NoStream but stream has events.

        // Insert each event
        for (var i = 0; i < events.Length; i++) {
            var evt = events[i];
            var idx = i.ToString();

            cmd.Parameters.AddWithValue($"@mid{idx}", evt.MessageId.ToString());
            cmd.Parameters.AddWithValue($"@mtype{idx}", evt.MessageType);
            cmd.Parameters.AddWithValue($"@jdata{idx}", evt.JsonData);
            cmd.Parameters.AddWithValue($"@jmeta{idx}", (object?)evt.JsonMetadata ?? DBNull.Value);

            // stream_position = (SELECT version FROM streams WHERE stream_name = @stream_name) + (i+1)
            // This will cause UNIQUE constraint violation if another writer has already appended
            sb.AppendLine($"""
                INSERT INTO {Schema.MessagesTable} (message_id, message_type, stream_id, stream_position, json_data, json_metadata, created)
                VALUES (
                    @mid{idx}, @mtype{idx},
                    (SELECT stream_id FROM {Schema.StreamsTable} WHERE stream_name = @stream_name),
                    (SELECT version FROM {Schema.StreamsTable} WHERE stream_name = @stream_name) + {i + 1},
                    @jdata{idx}, @jmeta{idx}, @created
                );
                """);
        }

        // 3. Update stream version
        var newVersionExpr = $"version + {eventCount}";
        sb.AppendLine($"UPDATE {Schema.StreamsTable} SET version = {newVersionExpr} WHERE stream_name = @stream_name;");

        // 4. Return new_version and last global_position
        sb.AppendLine($"""
            SELECT
                (SELECT version FROM {Schema.StreamsTable} WHERE stream_name = @stream_name) AS new_version,
                (SELECT MAX(global_position) FROM {Schema.MessagesTable} WHERE stream_id = (SELECT stream_id FROM {Schema.StreamsTable} WHERE stream_name = @stream_name)) AS global_position;
            """);

        cmd.CommandText = sb.ToString();
        return cmd;
    }

    protected override bool IsConflict(Exception exception) => exception is SqliteException { SqliteErrorCode: 19 };

    protected override DbCommand GetStreamExistsCommand(SqliteConnection connection, StreamName stream)
        => connection.GetTextCommand(Schema.StreamExists).Add("@name", stream.ToString());

    protected override DbCommand GetTruncateCommand(
            SqliteConnection       connection,
            StreamName             stream,
            ExpectedStreamVersion  expectedVersion,
            StreamTruncatePosition position
        ) {
        var sql = $"""
            DELETE FROM {Schema.MessagesTable}
            WHERE stream_id = (SELECT stream_id FROM {Schema.StreamsTable} WHERE stream_name = @stream_name)
            AND stream_position < @position
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@position", position.Value);
    }
}
```

Wait — I realize the above code block in the plan is getting unwieldy because I'm trying to think through the SQLite quirks inline. Let me write the clean final version.

The key insight: The base class `AppendEvents` calls `GetAppendCommand` then does `ExecuteReaderAsync` expecting `(int version, long globalPosition)` from `reader.GetInt64(1)` and `reader.GetInt32(0)`. So the command must return a result set with those two columns.

For SQLite, we build a multi-statement `CommandText`:
1. `INSERT OR IGNORE INTO streams ...` (idempotent stream creation)
2. For each event: `INSERT INTO messages ...` (with computed stream_position from current version)
3. `UPDATE streams SET version = ...`
4. `SELECT new_version, last_global_position` (this is what ExecuteReader returns)

The version validation happens naturally: if expected_version is wrong, the computed stream_position will collide with existing rows, causing UNIQUE constraint violation (error code 19).

But we also need to handle `expected_version = -1` (NoStream): if the stream already has events (version != -1), we should fail even if there's no position collision. We handle this by checking: if expected != -2 (Any) and expected != current_version, the first INSERT will compute stream_position = (current_version + 1) which may not collide. So we need an explicit check.

Solution: Add a version-check statement before the inserts that fails if the version is wrong:

```sql
-- Fails with 'WrongExpectedVersion' if version doesn't match
SELECT CASE
    WHEN @expected_version != -2
    AND (SELECT version FROM schema_streams WHERE stream_name = @stream_name) != @expected_version
    THEN RAISE(ABORT, 'WrongExpectedVersion')
END;
```

Wait, `RAISE` only works inside triggers in SQLite. We need another approach.

Alternative: Use a CHECK constraint trick or a deliberate constraint violation. Or, handle version validation in C# code before building the command.

Actually the simplest approach: **Don't try to make it a single command.** Instead, override the entire `AppendEvents` method. Looking at the base class again — `AppendEvents` in `SqlEventStoreBase` is implementing `IEventStore.AppendEvents` directly (not virtual), but since `SqliteStore` also implements `IEventStore` via the base class, we can use `new` to shadow it, or better: we can implement `IEventStore.AppendEvents` explicitly.

Actually, the cleanest approach given the architecture: just do the version check in the SQL using a subquery that inserts into a table that doesn't exist (causing an error), or use `CASE WHEN ... THEN (SELECT 1/0) END` to force a division-by-zero error. But those are hacks.

**Best approach for the plan**: implement `GetAppendCommand` to build a command that works within the base class flow, and handle version checking via the UNIQUE constraint on (stream_id, stream_position). For the NoStream case (-1), insert with stream_position = 0 — if stream already has event at position 0, it'll fail with constraint violation. For specific version, same logic. For Any (-2), just use current version + offset.

But the problem is: with expected_version = -1 (NoStream), if the stream already exists with version 5, the first insert tries position 6 (5+1), which doesn't conflict. So we'd incorrectly append to an existing stream.

**Final approach**: We need to handle the version check explicitly. Since `GetAppendCommand` builds the command before execution, and the base class flow is:
1. Open connection
2. Begin transaction
3. Build command via `GetAppendCommand`
4. Execute reader
5. Commit or rollback

We can add SQL statements before the inserts that will fail if version doesn't match. In SQLite, we can use this trick:

```sql
-- This INSERT will fail with UNIQUE constraint if version check fails
-- We create a temporary table for version checking
CREATE TEMP TABLE IF NOT EXISTS _version_check (id INTEGER PRIMARY KEY CHECK(id = 1));
INSERT INTO _version_check VALUES (
    CASE WHEN @expected_version = -2 THEN 1
         WHEN (SELECT version FROM schema_streams WHERE stream_name = @stream_name) = @expected_version THEN 1
         ELSE NULL -- NULL fails the CHECK(id = 1) constraint
    END
);
DELETE FROM _version_check;
```

Hmm, this is getting complicated. The simplest robust approach: **have `SqliteStore` not extend the base class for append**, or provide a helper method.

Actually, re-reading the base class more carefully:

```csharp
await using var cmd = GetAppendCommand(connection, (TTransaction)transaction, stream, expectedVersion, persistedEvents);
try {
    await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken)) {
        await reader.ReadAsync(cancellationToken);
        result = new((ulong)reader.GetInt64(1), reader.GetInt32(0));
    }
    await transaction.CommitAsync(cancellationToken);
```

The command must succeed and return a row. If it fails, the exception is caught and checked with `IsConflict`.

**Simplest correct approach for SQLite**: In `GetAppendCommand`, build SQL that:
1. Checks version and fails with constraint violation if wrong
2. Inserts events
3. Updates version
4. Returns result

For version checking without RAISE, use this pattern:

```sql
INSERT OR ABORT INTO __schema___messages (...)
SELECT ... WHERE (SELECT version FROM __schema___streams WHERE stream_name = @name) = @expected_version OR @expected_version = -2;
```

If the WHERE clause is false (version mismatch), zero rows are inserted, no error. Then we can check CHANGES() and fail. But we can't easily fail from SQL.

**OK, final decision**: The plan should have `SqliteStore` implement the append logic by duplicating the base class flow. This is the pragmatic approach. The store still extends `SqlEventStoreBase` for reads, truncate, and stream-exists, but overrides append.

Actually wait — I just realized `AppendEvents` on the base class is `public` and implements `IEventStore.AppendEvents`. In C#, we CAN override it by re-implementing the interface explicitly on the derived class. Let me check... No, the base class method isn't virtual, but we can use `new` keyword and it will work because the call site uses `IEventStore` interface.

Actually it won't work cleanly. The service resolves `IEventStore` and calls `AppendEvents`. If the base class implements it non-virtually, the derived class can only shadow it with `new`, which won't be called through the interface reference unless we also explicitly implement the interface.

Let me check if the base class method is virtual... Looking at the code: `public async Task<AppendEventsResult> AppendEvents(...)` — it's not virtual.

**The right solution**: Make the derived class explicitly implement `IEventStore.AppendEvents`:

```csharp
async Task<AppendEventsResult> IEventStore.AppendEvents(...) {
    // our custom implementation
}
```

This will be called when accessed through `IEventStore` interface, which is how it's always used.

For `GetAppendCommand`, we still need to provide an implementation (abstract method), so we throw `NotSupportedException` since it won't be called.

Let me finalize the approach in the plan code below.

**Step 1: Write SqliteStore.cs**

```csharp
// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Data.Common;
using Eventuous.Sql.Base;
using Eventuous.Sqlite.Extensions;

namespace Eventuous.Sqlite;

public record SqliteStoreOptions {
    public string ConnectionString   { get; init; } = "Data Source=eventuous.db";
    public string Schema             { get; init; } = Schema.DefaultSchema;
    public bool   InitializeDatabase { get; init; }
}

public class SqliteStore : SqlEventStoreBase<SqliteConnection, SqliteTransaction>, IEventStore {
    readonly GetSqliteConnection _getConnection;

    public Schema Schema { get; }

    public SqliteStore(SqliteStoreOptions options, IEventSerializer? serializer = null, IMetadataSerializer? metaSerializer = null)
        : base(serializer, metaSerializer) {
        var connectionString = Ensure.NotEmptyString(options.ConnectionString);
        _getConnection = ct => ConnectionFactory.GetConnection(connectionString, ct);
        Schema = new(options.Schema);
    }

    protected override async ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken)
        => await _getConnection(cancellationToken).NoContext();

    protected override DbCommand GetReadCommand(SqliteConnection connection, StreamName stream, StreamReadPosition start, int count) {
        var sql = $"""
            SELECT m.message_id, m.message_type, m.stream_position, m.global_position, m.json_data, m.json_metadata, m.created
            FROM {Schema.MessagesTable} m
            JOIN {Schema.StreamsTable} s ON m.stream_id = s.stream_id
            WHERE s.stream_name = @stream_name AND m.stream_position >= @from_position
            ORDER BY m.stream_position
            LIMIT @count
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@from_position", start.Value)
            .Add("@count", count);
    }

    protected override DbCommand GetReadBackwardsCommand(SqliteConnection connection, StreamName stream, StreamReadPosition start, int count) {
        var sql = $"""
            SELECT m.message_id, m.message_type, m.stream_position, m.global_position, m.json_data, m.json_metadata, m.created
            FROM {Schema.MessagesTable} m
            JOIN {Schema.StreamsTable} s ON m.stream_id = s.stream_id
            WHERE s.stream_name = @stream_name AND m.stream_position <= MIN(@from_position, s.version)
            ORDER BY m.stream_position DESC
            LIMIT @count
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@from_position", start.Value)
            .Add("@count", count);
    }

    protected override DbCommand GetAppendCommand(
        SqliteConnection connection, SqliteTransaction transaction, StreamName stream,
        ExpectedStreamVersion expectedVersion, NewPersistedEvent[] events
    ) => throw new NotSupportedException("Use the IEventStore.AppendEvents explicit implementation instead");

    // Explicit interface implementation to override the base class's non-virtual AppendEvents
    [RequiresDynamicCode("Calls serialization")]
    [RequiresUnreferencedCode("Calls serialization")]
    async Task<AppendEventsResult> IEventStore.AppendEvents(
        StreamName stream, ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<NewStreamEvent> events, CancellationToken cancellationToken
    ) {
        var serializer = GetSerializer();
        var metaSerializer = GetMetaSerializer();

        var persistedEvents = events.Where(x => x.Payload != null).Select(Convert).ToArray();

        await using var connection = await OpenConnection(cancellationToken).NoContext();
        await using var transaction = connection.BeginTransaction();

        try {
            // Step 1: Ensure stream exists
            await using (var ensureCmd = connection.GetTextCommand(
                $"INSERT OR IGNORE INTO {Schema.StreamsTable} (stream_name, version) VALUES (@stream_name, -1)",
                transaction
            )) {
                ensureCmd.Parameters.AddWithValue("@stream_name", stream.ToString());
                await ensureCmd.ExecuteNonQueryAsync(cancellationToken).NoContext();
            }

            // Step 2: Get current version
            int currentVersion;
            int streamId;
            await using (var checkCmd = connection.GetTextCommand(
                $"SELECT stream_id, version FROM {Schema.StreamsTable} WHERE stream_name = @stream_name",
                transaction
            )) {
                checkCmd.Parameters.AddWithValue("@stream_name", stream.ToString());
                await using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken).NoContext();

                if (!await reader.ReadAsync(cancellationToken).NoContext())
                    throw new StreamNotFound(stream);

                streamId = reader.GetInt32(0);
                currentVersion = reader.GetInt32(1);
            }

            // Step 3: Validate expected version
            if (expectedVersion.Value != -2) { // -2 = Any
                if (expectedVersion.Value != currentVersion)
                    throw new AppendToStreamException(stream, new Exception($"WrongExpectedVersion {expectedVersion.Value}, current version {currentVersion}"));
            }

            // Step 4: Insert events
            var created = DateTime.UtcNow.ToString("O");
            long lastGlobalPosition = 0;

            for (var i = 0; i < persistedEvents.Length; i++) {
                var evt = persistedEvents[i];
                await using var insertCmd = connection.GetTextCommand(
                    $"""
                    INSERT INTO {Schema.MessagesTable} (message_id, message_type, stream_id, stream_position, json_data, json_metadata, created)
                    VALUES (@mid, @mtype, @sid, @spos, @jdata, @jmeta, @created)
                    RETURNING global_position
                    """,
                    transaction
                );

                insertCmd.Parameters.AddWithValue("@mid", evt.MessageId.ToString());
                insertCmd.Parameters.AddWithValue("@mtype", evt.MessageType);
                insertCmd.Parameters.AddWithValue("@sid", streamId);
                insertCmd.Parameters.AddWithValue("@spos", currentVersion + i + 1);
                insertCmd.Parameters.AddWithValue("@jdata", evt.JsonData);
                insertCmd.Parameters.AddWithValue("@jmeta", (object?)evt.JsonMetadata ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@created", created);

                var result = await insertCmd.ExecuteScalarAsync(cancellationToken).NoContext();
                lastGlobalPosition = (long)result!;
            }

            // Step 5: Update stream version
            var newVersion = currentVersion + persistedEvents.Length;
            await using (var updateCmd = connection.GetTextCommand(
                $"UPDATE {Schema.StreamsTable} SET version = @new_version WHERE stream_id = @stream_id",
                transaction
            )) {
                updateCmd.Parameters.AddWithValue("@new_version", newVersion);
                updateCmd.Parameters.AddWithValue("@stream_id", streamId);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).NoContext();
            }

            await transaction.CommitAsync(cancellationToken).NoContext();

            return new((ulong)lastGlobalPosition, newVersion);
        } catch (AppendToStreamException) {
            await transaction.RollbackAsync(cancellationToken).NoContext();
            throw;
        } catch (Exception e) {
            await transaction.RollbackAsync(cancellationToken).NoContext();

            if (e is SqliteException { SqliteErrorCode: 19 })
                throw new AppendToStreamException(stream, e);

            throw;
        }

        NewPersistedEvent Convert(NewStreamEvent evt) {
            var data = serializer.SerializeEvent(evt.Payload!);
            var meta = metaSerializer.Serialize(evt.Metadata);
            return new(evt.Id, data.EventType, System.Text.Encoding.UTF8.GetString(data.Payload), System.Text.Encoding.UTF8.GetString(meta));
        }
    }

    // We need accessors for the serializers from the base class
    internal IEventSerializer GetSerializer() => /* need to expose from base */;
    internal IMetadataSerializer GetMetaSerializer() => /* need to expose from base */;

    protected override bool IsStreamNotFound(Exception exception) =>
        exception is SqliteException e && e.Message.Contains("StreamNotFound");

    protected override bool IsConflict(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: 19 };

    protected override DbCommand GetStreamExistsCommand(SqliteConnection connection, StreamName stream)
        => connection.GetTextCommand(Schema.StreamExists).Add("@name", stream.ToString());

    protected override DbCommand GetTruncateCommand(
        SqliteConnection connection, StreamName stream,
        ExpectedStreamVersion expectedVersion, StreamTruncatePosition position
    ) {
        var sql = $"""
            DELETE FROM {Schema.MessagesTable}
            WHERE stream_id = (SELECT stream_id FROM {Schema.StreamsTable} WHERE stream_name = @stream_name)
            AND stream_position < @position
            """;

        return connection.GetTextCommand(sql)
            .Add("@stream_name", stream.ToString())
            .Add("@position", position.Value);
    }
}
```

Hmm, there's a problem: the base class stores `_serializer` and `_metaSerializer` as private readonly fields. We can't access them from the derived class for the explicit interface implementation.

**Options:**
1. Duplicate the serializer fields in the derived class (pass them to base AND store locally)
2. Make the base class fields protected (requires modifying Sql.Base — not desirable)
3. Store our own references in the constructor

Option 1 is simplest. We'll pass serializers to the base and also keep our own references.

OK I realize this plan document is getting extremely long because I'm working through design problems inline. Let me finalize my thinking and write a clean, concise plan.

**Step 2: Commit**

```bash
git add src/Sqlite/
git commit -m "feat: add SqliteStore event store implementation"
```

---

OK, I need to step back. The plan document is becoming a design document with inline problem-solving rather than a clear implementation plan. Let me write the final clean version now.

**Step 2: Commit**

```bash
git add src/Sqlite/
git commit -m "feat: add SqliteStore event store implementation"
```

---

### Task 5: Create SchemaInitializer and DI Registration Extensions

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/SchemaInitializer.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Extensions/RegistrationExtensions.cs`

**Step 1: Create SchemaInitializer** — follows SQL Server pattern but catches `SqliteException`

**Step 2: Create RegistrationExtensions** — `AddEventuousSqlite(connectionString, schema, initializeDatabase)` and `AddSqliteCheckpointStore()`

**Step 3: Commit**

---

### Task 6: Create Subscription classes

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/Subscriptions/SqliteSubscriptionBase.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Subscriptions/SqliteAllStreamSubscription.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Subscriptions/SqliteStreamSubscription.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Subscriptions/SqliteCheckpointStore.cs`

Follows SQL Server pattern. Key differences:
- `IsTransient` always returns false (SQLite is embedded, no transient network errors)
- `PrepareCommand` uses text SQL instead of stored procs
- `SqliteCheckpointStore` uses `ConnectionFactory.GetConnection`

---

### Task 7: Create SqliteProjector

**Files:**
- Create: `src/Sqlite/src/Eventuous.Sqlite/Projections/SqliteProjector.cs`
- Create: `src/Sqlite/src/Eventuous.Sqlite/Projections/SqliteConnectionOptions.cs`

Follows SQL Server `SqlServerProjector` pattern.

---

### Task 8: Add to solution file and verify build

**Files:**
- Modify: `Eventuous.slnx`

Add the new project under `/Relational/Sqlite/` folder.

Run: `dotnet build src/Sqlite/src/Eventuous.Sqlite/Eventuous.Sqlite.csproj`

---

### Task 9: Create test project and StoreFixture

**Files:**
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Eventuous.Tests.Sqlite.csproj`
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Fixtures/SqliteFixture.cs`

Test project references base test libraries. The fixture creates a temp file-based SQLite database (no Testcontainers needed). SQLite tests don't need a Docker container.

**Important**: The `StoreFixtureBase<TContainer>` requires a `DockerContainer` type parameter. Since SQLite doesn't need Docker, we need a different approach. We can either:
- Create a minimal `StoreFixtureBase` subclass that doesn't use containers
- Or use a mock/fake container

Looking at the base class, `StoreFixtureBase<TContainer>` calls `Container.StartAsync()` and `Container.DisposeAsync()`. For SQLite we just need a temp file. We should create our own fixture that extends `StoreFixtureBase` (the non-generic base) directly.

---

### Task 10: Create Store tests

**Files:**
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Store/StoreFixture.cs`
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Store/StoreTests.cs`

---

### Task 11: Create Subscription tests

**Files:**
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Subscriptions/SubscriptionFixture.cs`
- Create: `src/Sqlite/test/Eventuous.Tests.Sqlite/Subscriptions/SubscribeTests.cs`

---

### Task 12: Add test project to solution and run tests

**Files:**
- Modify: `Eventuous.slnx`

Run: `dotnet test src/Sqlite/test/Eventuous.Tests.Sqlite/Eventuous.Tests.Sqlite.csproj`

---

### Task 13: Final commit

```bash
git add -A
git commit -m "feat: add SQLite event store implementation with tests"
```
