// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.KurrentDB.Subscriptions;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Checkpoints;
using KurrentDBClient = global::KurrentDB.Client.KurrentDBClient;
using KurrentDBClientSettings = global::KurrentDB.Client.KurrentDBClientSettings;

namespace Eventuous.Tests.KurrentDB.Subscriptions;

public class EndOfStreamSourceKeyTests {
    [Test]
    public async Task Derived_subscriptions_keep_independent_reads() {
        using var client = new KurrentDBClient(KurrentDBClientSettings.Create("esdb://localhost:2113?tls=false"));
        await using var all = new DerivedAll(client);
        await using var stream = new DerivedStream(client);
        await using var persistentAll = new DerivedPersistentAll(client);
        await using var persistentStream = new DerivedPersistentStream(client);
        foreach (IMeasuredSubscription subscription in new IMeasuredSubscription[] { all, stream, persistentAll, persistentStream })
            await Assert.That(subscription.EndOfStreamSourceKey).IsNull();
    }

    sealed class DerivedAll(KurrentDBClient client) : AllStreamSubscription(client, "derived-all", new NoOpCheckpointStore(), new());
    sealed class DerivedPersistentAll(KurrentDBClient client) : AllPersistentSubscription(client, "derived-persistent-all", new());
    sealed class DerivedStream(KurrentDBClient client) : StreamSubscription(
        client, new() { SubscriptionId = "derived-stream", StreamName = new("one") }, new NoOpCheckpointStore(), new());
    sealed class DerivedPersistentStream(KurrentDBClient client) : StreamPersistentSubscription(
        client, new() { SubscriptionId = "derived-persistent-stream", StreamName = new("one") }, new());

    [Test]
    public async Task Keys_share_catchup_and_persistent_sources_but_separate_clients_and_streams() {
        using var client = new KurrentDBClient(KurrentDBClientSettings.Create("esdb://localhost:2113?tls=false"));
        using var otherClient = new KurrentDBClient(KurrentDBClientSettings.Create("esdb://localhost:2114?tls=false"));
        await using var all = new AllStreamSubscription(client, "all", new NoOpCheckpointStore(), new());
        await using var persistentAll = new AllPersistentSubscription(client, "persistent-all", new());
        await using var other = new AllStreamSubscription(otherClient, "other", new NoOpCheckpointStore(), new());
        await using var stream = new StreamSubscription(client, new StreamSubscriptionOptions { SubscriptionId = "stream", StreamName = new("one") }, new NoOpCheckpointStore(), new());
        await using var persistentStream = new StreamPersistentSubscription(client, new StreamPersistentSubscriptionOptions { SubscriptionId = "persistent-stream", StreamName = new("one") }, new());
        await using var otherStream = new StreamSubscription(client, new StreamSubscriptionOptions { SubscriptionId = "other-stream", StreamName = new("two") }, new NoOpCheckpointStore(), new());

        await Assert.That(all.EndOfStreamSourceKey).IsEqualTo(persistentAll.EndOfStreamSourceKey);
        await Assert.That(all.EndOfStreamSourceKey).IsNotEqualTo(other.EndOfStreamSourceKey);
        await Assert.That(all.EndOfStreamSourceKey).IsNotEqualTo(stream.EndOfStreamSourceKey);
        await Assert.That(stream.EndOfStreamSourceKey).IsEqualTo(persistentStream.EndOfStreamSourceKey);
        await Assert.That(stream.EndOfStreamSourceKey).IsNotEqualTo(otherStream.EndOfStreamSourceKey);
    }
}
