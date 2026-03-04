using Eventuous.Sqlite.Subscriptions;
using Eventuous.Sut.App;
using Eventuous.Tests.Persistence.Base.Fixtures;
using Eventuous.Tests.Subscriptions.Base;

namespace Eventuous.Tests.Sqlite.Subscriptions;

[NotInParallel]
public class HandlerFailureResubscribe() : SubscriptionTestBase(Fixture) {
    static readonly SubscriptionFixture<SqliteAllStreamSubscription, SqliteAllStreamSubscriptionOptions, FailOnceEventHandler> Fixture
        = new(opt => opt.ThrowOnError = true, false);

    [Test]
    public async Task Sqlite_ShouldResubscribeAfterHandlerFailure(CancellationToken cancellationToken) {
        const int count = 10;

        var commands = Enumerable
            .Range(0, count)
            .Select(_ => DomainFixture.CreateImportBooking())
            .ToList();

        var service = new BookingService(Fixture.EventStore);

        foreach (var cmd in commands) {
            var result = await service.Handle(cmd, default);
            result.ThrowIfError();
        }

        await Fixture.StartSubscription();

        // Wait for the handler to fail and then recover after resubscription.
        // Timeout accounts for: initial processing + 2s resubscription delay + reprocessing.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try {
            while (!cts.Token.IsCancellationRequested) {
                if (Fixture.Handler is { HasFailed: true, HandledAfterFailure: > 0 }) break;

                await Task.Delay(100, cts.Token);
            }
        } catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) {
            // Fall through to assertions
        }

        await Fixture.StopSubscription();

        await Assert.That(Fixture.Handler.HasFailed).IsTrue();
        await Assert.That(Fixture.Handler.HandledAfterFailure).IsGreaterThan(0);

        WriteLine(
            "Handler failed once, then processed {0} more events after resubscription",
            Fixture.Handler.HandledAfterFailure
        );
    }
}
