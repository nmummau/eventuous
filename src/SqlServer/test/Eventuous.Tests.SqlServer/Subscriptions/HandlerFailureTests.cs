using Eventuous.SqlServer.Subscriptions;
using Eventuous.Tests.Subscriptions.Base;
using Testcontainers.MsSql;

namespace Eventuous.Tests.SqlServer.Subscriptions;

[NotInParallel]
public class HandlerFailureResubscribe()
    : HandlerFailureBase<MsSqlContainer, SqlServerAllStreamSubscription, SqlServerAllStreamSubscriptionOptions, SqlServerCheckpointStore>(
        new SubscriptionFixture<SqlServerAllStreamSubscription, SqlServerAllStreamSubscriptionOptions, FailOnceEventHandler>(
            opt => opt.ThrowOnError = true,
            false
        )
    ) {
    [Test]
    public async Task SqlServer_ShouldResubscribeAfterHandlerFailure(CancellationToken cancellationToken) {
        await ShouldResubscribeAfterHandlerFailure(cancellationToken);
    }
}
