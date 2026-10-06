using System.Data;
using Eventuous.Sqlite.Projections;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Eventuous.Tests.Sqlite.Projections;

public class SqliteProjectorTests {
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ShouldProjectMultipleEventsWithParametersAndContext() {
        await using var database = await Database.Create();
        var projector = new ReadModelProjector(database.Options);

        (await projector.HandleEvent(Context(new Recorded("first", "O'Brien"), 12))).ShouldBe(EventHandlingStatus.Success);
        (await projector.HandleEvent(Context(new Recorded("second", "Robert'); DROP TABLE read_model;--"), 13))).ShouldBe(EventHandlingStatus.Success);

        (await database.Rows()).ShouldBe(new[] {
            new Row("first", "O'Brien", "booking-42", 12),
            new Row("second", "Robert'); DROP TABLE read_model;--", "booking-42", 13)
        });
        projector.Connection!.State.ShouldBe(ConnectionState.Closed);
    }

    [Test]
    public async Task ShouldAwaitCommandBuilderBeforeWriting() {
        await using var database = await Database.Create();
        var entered = Signal();
        var release = Signal();
        var projector = new ReadModelProjector(database.Options, async ctx => {
            entered.TrySetResult();
            await release.Task.WaitAsync(Timeout, ctx.CancellationToken);
        });
        var handling = projector.HandleEvent(Context(new Deferred("deferred", "ready"))).AsTask();

        try {
            await entered.Task.WaitAsync(Timeout);
            handling.IsCompleted.ShouldBeFalse();
            (await database.Rows()).ShouldBeEmpty();
        }
        finally {
            release.TrySetResult();
            await handling.WaitAsync(Timeout);
        }

        (await database.Rows()).ShouldBe(new[] { new Row("deferred", "ready", "booking-42", 0) });
        projector.Connection!.State.ShouldBe(ConnectionState.Closed);
    }

    [Test]
    public async Task ShouldPropagateCommandBuilderFailureWithoutWriting() {
        await using var database = await Database.Create();
        var failure = new InvalidOperationException("Cannot build read model");
        var projector = new ReadModelProjector(database.Options, _ => Task.FromException(failure));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => projector.HandleEvent(Context(new Deferred("failed", "ignored"))).AsTask());

        thrown.ShouldBeSameAs(failure);
        (await database.Rows()).ShouldBeEmpty();
        projector.Connection!.State.ShouldBe(ConnectionState.Closed);
        await projector.HandleEvent(Context(new Recorded("valid", "next")));
        (await database.Rows()).Single().Id.ShouldBe("valid");
    }

    [Test]
    public async Task ShouldPropagateSqlFailureAndAllowSubsequentProjection() {
        await using var database = await Database.Create();
        var projector = new ReadModelProjector(database.Options);
        await projector.HandleEvent(Context(new Recorded("duplicate", "original")));

        var thrown = await Should.ThrowAsync<SqliteException>(() => projector.HandleEvent(Context(new Recorded("duplicate", "replacement"))).AsTask());

        thrown.SqliteErrorCode.ShouldBe(19); // UNIQUE constraint, not a swallowed or unrelated failure.
        projector.Connection!.State.ShouldBe(ConnectionState.Closed);
        await projector.HandleEvent(Context(new Recorded("valid", "next"), 1));
        (await database.Rows()).ShouldBe(new[] {
            new Row("duplicate", "original", "booking-42", 0), new Row("valid", "next", "booking-42", 1)
        });
    }

    [Test]
    public async Task ShouldCancelPendingBuilderWithoutExecutingItsCommand() {
        await using var database = await Database.Create();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var release = Signal();
        var projector = new ReadModelProjector(database.Options, async _ => {
            entered.TrySetResult();
            // Deliberately finish building after cancellation: execution must still honor the token.
            await release.Task.WaitAsync(Timeout);
        });
        var handling = projector.HandleEvent(Context(new Deferred("cancelled", "ignored"), cancellationToken: cancellation.Token)).AsTask();
        try {
            await entered.Task.WaitAsync(Timeout);
            cancellation.Cancel();
        }
        finally {
            release.TrySetResult();
        }

        await Should.ThrowAsync<OperationCanceledException>(() => handling.WaitAsync(Timeout));
        (await database.Rows()).ShouldBeEmpty();
        projector.Connection!.State.ShouldBe(ConnectionState.Closed);
        await projector.HandleEvent(Context(new Recorded("valid", "next")));
        (await database.Rows()).Single().Id.ShouldBe("valid");
    }

    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static MessageConsumeContext Context(object message, ulong position = 0, CancellationToken cancellationToken = default)
        => new(Guid.NewGuid().ToString(), message.GetType().Name, "application/json", "booking-42", position,
            position, position + 100, position, DateTime.UtcNow, message, null, "read-model", cancellationToken);

    public record Recorded(string Id, string Value);
    public record Deferred(string Id, string Value);
    record Row(string Id, string Value, string Stream, long Position);

    class ReadModelProjector : SqliteProjector {
        public SqliteConnection? Connection { get; private set; }

        public ReadModelProjector(SqliteConnectionOptions options, Func<IMessageConsumeContext, Task>? beforeCommand = null) : base(options) {
            On<Recorded>((connection, ctx) => Build(connection, ctx, ctx.Message.Id, ctx.Message.Value));
            On<Deferred>(async (connection, ctx) => {
                Connection = connection;
                if (beforeCommand != null) await beforeCommand(ctx);
                return Build(connection, ctx, ctx.Message.Id, ctx.Message.Value);
            });
        }

        SqliteCommand Build(SqliteConnection connection, IMessageConsumeContext context, string id, string value) {
            Connection = connection;
            return Project(connection, "INSERT INTO read_model (id, value, stream, position) VALUES ($id, $value, $stream, $position)",
                new("$id", id), new("$value", value), new("$stream", context.Stream.ToString()), new("$position", (long)context.StreamPosition));
        }
    }

    sealed class Database : IAsyncDisposable {
        readonly string _path = Path.Combine(Path.GetTempPath(), $"eventuous-projection-{Guid.NewGuid():N}.db");
        public SqliteConnectionOptions Options => new($"Data Source={_path};Pooling=False", "main");

        public static async Task<Database> Create() {
            var database = new Database();
            try {
                await using var connection = new SqliteConnection(database.Options.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE read_model (id TEXT PRIMARY KEY, value TEXT NOT NULL, stream TEXT NOT NULL, position INTEGER NOT NULL)";
                await command.ExecuteNonQueryAsync();
                return database;
            }
            catch {
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<Row[]> Rows() {
            await using var connection = new SqliteConnection(Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, value, stream, position FROM read_model ORDER BY position, id";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Row>();
            while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            return rows.ToArray();
        }

        public ValueTask DisposeAsync() {
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
            return ValueTask.CompletedTask;
        }
    }
}
