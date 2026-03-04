using Eventuous.Postgresql;
using Eventuous.Postgresql.Subscriptions;
using Eventuous.Tests.Subscriptions.Base;
using Testcontainers.PostgreSql;

namespace Eventuous.Tests.Postgres.Subscriptions;

[NotInParallel]
public class HandlerFailureResubscribe()
    : HandlerFailureBase<PostgreSqlContainer, PostgresAllStreamSubscription, PostgresAllStreamSubscriptionOptions, PostgresCheckpointStore>(
        new SubscriptionFixture<PostgresStore, PostgresAllStreamSubscription, PostgresAllStreamSubscriptionOptions, FailOnceEventHandler>(
            opt => opt.ThrowOnError = true,
            false
        )
    ) {
    [Test]
    public async Task Postgres_ShouldResubscribeAfterHandlerFailure(CancellationToken cancellationToken) {
        await ShouldResubscribeAfterHandlerFailure(cancellationToken);
    }
}
