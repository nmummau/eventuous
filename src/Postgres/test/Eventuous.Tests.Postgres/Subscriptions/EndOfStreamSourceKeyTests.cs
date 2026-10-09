// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.Postgresql;
using Eventuous.Postgresql.Subscriptions;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Checkpoints;
using Npgsql;

namespace Eventuous.Tests.Postgres.Subscriptions;

public class EndOfStreamSourceKeyTests {
    [Test]
    public async Task Keys_separate_data_sources_schemas_and_streams() {
        await using var source = NpgsqlDataSource.Create("Host=localhost;Database=one");
        await using var otherSource = NpgsqlDataSource.Create("Host=localhost;Database=two");
        await using var first = All(source, "first", "schema");
        await using var same = All(source, "second", "schema");
        await using var other = All(otherSource, "third", "schema");
        await using var otherSchema = All(source, "fourth", "other");
        await using var overridden = new PostgresAllStreamSubscription(source,
            new() { SubscriptionId = "override", Schema = "ignored" }, new NoOpCheckpointStore(), new(), storeOptions: new("schema"));
        await using var stream = Stream(source, "stream1", "one");
        await using var sameStream = Stream(source, "stream2", "one");
        await using var otherStream = Stream(source, "stream3", "two");

        await Assert.That(first.EndOfStreamSourceKey).IsEqualTo(same.EndOfStreamSourceKey);
        await Assert.That(first.EndOfStreamSourceKey).IsEqualTo(overridden.EndOfStreamSourceKey);
        await Assert.That(first.EndOfStreamSourceKey).IsNotEqualTo(other.EndOfStreamSourceKey);
        await Assert.That(first.EndOfStreamSourceKey).IsNotEqualTo(otherSchema.EndOfStreamSourceKey);
        await Assert.That(first.EndOfStreamSourceKey).IsNotEqualTo(stream.EndOfStreamSourceKey);
        await Assert.That(stream.EndOfStreamSourceKey).IsEqualTo(sameStream.EndOfStreamSourceKey);
        await Assert.That(stream.EndOfStreamSourceKey).IsNotEqualTo(otherStream.EndOfStreamSourceKey);
    }

    [Test]
    public async Task Derived_subscriptions_keep_independent_reads() {
        await using var source = NpgsqlDataSource.Create("Host=localhost;Database=one");
        await using var all = new DerivedAll(source);
        await using var stream = new DerivedStream(source);
        await Assert.That(((IMeasuredSubscription)all).EndOfStreamSourceKey).IsNull();
        await Assert.That(((IMeasuredSubscription)stream).EndOfStreamSourceKey).IsNull();
    }

    sealed class DerivedAll(NpgsqlDataSource source) : PostgresAllStreamSubscription(
        source, new() { SubscriptionId = "derived-all" }, new NoOpCheckpointStore(), new());

    sealed class DerivedStream(NpgsqlDataSource source) : PostgresStreamSubscription(
        source, new() { SubscriptionId = "derived-stream", Stream = new("one") }, new NoOpCheckpointStore(), new());

    static PostgresAllStreamSubscription All(NpgsqlDataSource source, string id, string schema)
        => new(source, new() { SubscriptionId = id, Schema = schema }, new NoOpCheckpointStore(), new());

    static PostgresStreamSubscription Stream(NpgsqlDataSource source, string id, string stream)
        => new(source, new() { SubscriptionId = id, Schema = "schema", Stream = new(stream) }, new NoOpCheckpointStore(), new());
}
