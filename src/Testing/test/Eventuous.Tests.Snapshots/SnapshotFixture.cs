using System.Text.Json;
using Eventuous.KurrentDB;
using Eventuous.MongoDB.Snapshots;
using Eventuous.Postgresql;
using Eventuous.Postgresql.Snapshots;
using Eventuous.Redis.Snapshots;
using Eventuous.SqlServer.Snapshots;
using KurrentDB.Client;
using MongoDB.Driver;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.KurrentDb;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using TUnit.Core.Interfaces;

namespace Eventuous.Tests.Snapshots;

public enum Backend { Postgres, Redis, Mongo, SqlServer }
public enum EventBackend { Postgres, Kurrent }

// Shared only within a test process. Testcontainers allocates random host ports,
// and every test uses unique stream names. No external database is touched.
public sealed class SnapshotFixture : IAsyncInitializer, IAsyncDisposable {
    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:14").WithDatabase("snapshot_tests").Build();
    readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7.0.12-alpine").Build();
    readonly MongoDbContainer _mongo = new MongoDbBuilder().WithImage("mongo:7.0").Build();
    readonly MsSqlContainer _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
    readonly KurrentDbContainer _kurrent = new KurrentDbBuilder().WithImage("kurrentplatform/kurrentdb:25.1.3")
        .WithEnvironment("KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP", "true").Build();
    NpgsqlDataSource? _dataSource;
    KurrentDBClient? _client;
    ConnectionMultiplexer? _multiplexer;
    public TypeMapper Types { get; } = new();
    public IEventSerializer Serializer { get; private set; } = null!;
    public IEventStore Postgres { get; private set; } = null!;
    public IEventStore Kurrent { get; private set; } = null!;
    public IDatabase RedisDb => _multiplexer!.GetDatabase();
    public NpgsqlDataSource DataSource => _dataSource!;
    readonly Dictionary<Backend, ISnapshotStore> _stores = new();
    public ISnapshotStore Store(Backend backend) => _stores[backend];
    public IEventStore Events(EventBackend backend) => backend == EventBackend.Postgres ? Postgres : Kurrent;

    public async Task InitializeAsync() {
        Types.AddType<Added>("snapshot-tests.added.v1");
        Types.AddType<LedgerSnapshot>("snapshot-tests.snapshot.v1");
        Serializer = new DefaultEventSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web), Types);
        SnapshotTypeMap.Register(typeof(LedgerState<Same>), typeof(LedgerSnapshot), SnapshotStorageStrategy.SameStream);
        SnapshotTypeMap.Register(typeof(LedgerState<Separate>), typeof(LedgerSnapshot), SnapshotStorageStrategy.SeparateStream);
        SnapshotTypeMap.Register(typeof(LedgerState<External>), typeof(LedgerSnapshot), SnapshotStorageStrategy.SeparateStore);

        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Task.WhenAll(_postgres.StartAsync(startup.Token), _redis.StartAsync(startup.Token),
            _mongo.StartAsync(startup.Token), _sql.StartAsync(startup.Token), _kurrent.StartAsync(startup.Token));

        var builder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        builder.MapComposite<Eventuous.Sql.Base.NewPersistedEvent>(Schema.GetStreamMessageTypeName());
        _dataSource = builder.Build();
        await new Schema().CreateSchema(_dataSource, null, startup.Token);
        await new Eventuous.Postgresql.Snapshots.SnapshotSchema("snapshot_tests").CreateSchema(_dataSource, null, startup.Token);
        Postgres = new PostgresStore(_dataSource, null, Serializer);
        _stores[Backend.Postgres] = new PostgresSnapshotStore(_dataSource, new("snapshot_tests"), Serializer);

        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _stores[Backend.Redis] = new RedisSnapshotStore(() => RedisDb, serializer: Serializer);
        _client = new KurrentDBClient(KurrentDBClientSettings.Create(_kurrent.GetConnectionString()));
        Kurrent = new KurrentDBEventStore(_client, Serializer);

        var db = new MongoClient(_mongo.GetConnectionString()).GetDatabase("snapshot_tests");
        _stores[Backend.Mongo] = new MongoSnapshotStore(db, null, Serializer);

        await new Eventuous.SqlServer.Snapshots.SnapshotSchema("snapshot_tests").CreateSchema(_sql.GetConnectionString(), null, startup.Token);
        _stores[Backend.SqlServer] = new SqlServerSnapshotStore(_sql.GetConnectionString(), new("snapshot_tests"), Serializer);
    }

    public async ValueTask DisposeAsync() {
        if (_client != null) await _client.DisposeAsync();
        if (_multiplexer != null) await _multiplexer.DisposeAsync();
        if (_dataSource != null) await _dataSource.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(),
            _mongo.DisposeAsync().AsTask(), _sql.DisposeAsync().AsTask(), _kurrent.DisposeAsync().AsTask());
    }
}
