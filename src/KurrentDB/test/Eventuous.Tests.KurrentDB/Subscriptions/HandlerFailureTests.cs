using Eventuous.KurrentDB.Subscriptions;
using Eventuous.Tests.KurrentDB.Subscriptions.Fixtures;
using Eventuous.Tests.Subscriptions.Base;
using Testcontainers.KurrentDb;

namespace Eventuous.Tests.KurrentDB.Subscriptions;

public class HandlerFailureResubscribe()
    : HandlerFailureBase<KurrentDbContainer, AllStreamSubscription, AllStreamSubscriptionOptions, TestCheckpointStore>(
        new CatchUpSubscriptionFixture<AllStreamSubscription, AllStreamSubscriptionOptions, FailOnceEventHandler>(
            opt => opt.ThrowOnError = true,
            new("$all"),
            false
        )
    ) {
    [Test]
    [Retry(3)]
    public async Task Esdb_ShouldResubscribeAfterHandlerFailure(CancellationToken cancellationToken) {
        await ShouldResubscribeAfterHandlerFailure(cancellationToken);
    }
}
