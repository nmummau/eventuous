// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.Sut.Domain;
using Eventuous.Tests.Persistence.Base.Fixtures;
using Microsoft.Data.SqlClient;

namespace Eventuous.Tests.SqlServer.Store;

/// <summary>
/// Two appends with <see cref="ExpectedStreamVersion.Any"/> to one stream must both succeed. A third connection holds a lock
/// that stops both appends after check_stream reads the stream and before either one writes, so both appends read the same
/// stream state on every run instead of by chance.
/// </summary>
[ClassDataSource<StoreFixture>]
public class ConcurrentAppendTests {
    static readonly TimeSpan BlockTimeout = TimeSpan.FromSeconds(10);

    readonly StoreFixture _fixture;

    public ConcurrentAppendTests(StoreFixture fixture) {
        fixture.TypeMapper.RegisterKnownEventTypes(typeof(BookingEvents.BookingImported).Assembly);
        _fixture = fixture;
    }

    [Test]
    [Category("Store")]
    public async Task ShouldAppendWithAnyWhenTwoAppendsReadAnExistingStreamTogether() {
        var stream = Helpers.GetStreamName();
        await _fixture.AppendEvent(stream, Helpers.CreateEvent(), ExpectedStreamVersion.NoStream);

        // Stops every insert into Messages. check_stream reads only Streams, so it still runs.
        var failures = await AppendTwiceBehindGate(stream, $"SELECT COUNT(*) FROM {_fixture.SchemaName}.Messages WITH (TABLOCKX);");

        await Assert.That(failures).IsEmpty();
        await Assert.That(await CountEvents(stream)).IsEqualTo(3);
    }

    [Test]
    [Category("Store")]
    public async Task ShouldNotLeaveGlobalPositionGapWhenTwoAppendsReadAnExistingStreamTogether() {
        // A rolled back insert still uses its identity value, and the gap it leaves holds the all-stream subscriptions.
        var stream = Helpers.GetStreamName();
        await _fixture.AppendEvent(stream, Helpers.CreateEvent(), ExpectedStreamVersion.NoStream);
        var identityBefore = await CurrentMessagesIdentity();

        await AppendTwiceBehindGate(stream, $"SELECT COUNT(*) FROM {_fixture.SchemaName}.Messages WITH (TABLOCKX);");

        var storedByTheTwoAppends = await CountEvents(stream) - 1;
        await Assert.That(await CurrentMessagesIdentity() - identityBefore).IsEqualTo(storedByTheTwoAppends);
    }

    [Test]
    [Category("Store")]
    public async Task ShouldAppendWithAnyWhenTwoAppendsCreateTheSameStreamTogether() {
        var stream = Helpers.GetStreamName();

        // Stops the insert of the stream row. A read of the stream without lock hints still runs.
        var failures = await AppendTwiceBehindGate(
            stream,
            $"SELECT StreamId FROM {_fixture.SchemaName}.Streams WITH (UPDLOCK, HOLDLOCK) WHERE StreamName = @stream_name;"
        );

        await Assert.That(failures).IsEmpty();
        await Assert.That(await CountEvents(stream)).IsEqualTo(2);
    }

    /// <summary>
    /// Holds the lock of <paramref name="gateSql"/>, starts two appends, waits until both are blocked, then releases the lock.
    /// Returns the message of each append that failed.
    /// </summary>
    async Task<List<string>> AppendTwiceBehindGate(StreamName stream, string gateSql) {
        await using var gate = new SqlConnection(_fixture.Container.GetConnectionString());
        await gate.OpenAsync();
        await using var gateTransaction = (SqlTransaction)await gate.BeginTransactionAsync();

        await using (var hold = new SqlCommand(gateSql, gate, gateTransaction)) {
            hold.Parameters.AddWithValue("@stream_name", stream.ToString());
            await hold.ExecuteNonQueryAsync();
        }

        Task[] appends = [
            Task.Run(() => _fixture.AppendEvent(stream, Helpers.CreateEvent(), ExpectedStreamVersion.Any)),
            Task.Run(() => _fixture.AppendEvent(stream, Helpers.CreateEvent(), ExpectedStreamVersion.Any))
        ];
        await WaitUntilAppendsAreBlocked(appends.Length);
        await gateTransaction.RollbackAsync();

        var failures = new List<string>();

        foreach (var append in appends) {
            try {
                await append;
            } catch (Exception e) {
                failures.Add(e.GetBaseException().Message);
            }
        }

        return failures;
    }

    async Task WaitUntilAppendsAreBlocked(int count) {
        var sql = $"""
                   SELECT COUNT(*)
                   FROM sys.dm_exec_requests AS r
                   CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
                   WHERE r.blocking_session_id <> 0
                     AND t.dbid = DB_ID()
                     AND t.objectid IN (OBJECT_ID(N'{_fixture.SchemaName}.append_events'), OBJECT_ID(N'{_fixture.SchemaName}.check_stream'));
                   """;

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);

        var deadline = DateTime.UtcNow + BlockTimeout;

        while ((int)(await command.ExecuteScalarAsync())! < count) {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"The {count} appends were not blocked within {BlockTimeout}.");

            await Task.Delay(20);
        }
    }

    async Task<decimal> CurrentMessagesIdentity() {
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT IDENT_CURRENT('{_fixture.SchemaName}.Messages');", connection);

        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    async Task<int> CountEvents(StreamName stream) {
        var events = await _fixture.EventStore.ReadEvents(stream, StreamReadPosition.Start, 100, true, default);

        return events.Length;
    }
}
