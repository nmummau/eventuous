using DotNet.Testcontainers.Containers;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Checkpoints;
using Eventuous.Sut.App;
using Eventuous.Tests.Persistence.Base.Fixtures;

namespace Eventuous.Tests.Subscriptions.Base;

/// <summary>
/// Base test class that verifies subscriptions properly drop and resubscribe when a handler throws
/// an exception with ThrowOnError enabled. Reproduces the issue from GitHub #407.
/// </summary>
public abstract class HandlerFailureBase<TContainer, TSubscription, TSubscriptionOptions, TCheckpointStore>(
        SubscriptionFixtureBase<TContainer, TSubscription, TSubscriptionOptions, TCheckpointStore, FailOnceEventHandler> fixture
    ) : SubscriptionTestBase(fixture)
    where TContainer : DockerContainer
    where TSubscription : EventSubscription<TSubscriptionOptions>
    where TSubscriptionOptions : SubscriptionOptions
    where TCheckpointStore : class, ICheckpointStore {
    /// <summary>
    /// Produces events, starts a subscription with ThrowOnError and a handler that fails once,
    /// then verifies that the subscription resubscribes and continues processing events.
    /// </summary>
    protected async Task ShouldResubscribeAfterHandlerFailure(CancellationToken cancellationToken) {
        const int count = 10;

        await GenerateAndHandleCommands(count);
        await fixture.StartSubscription();

        // Wait for the handler to fail and then recover after resubscription.
        // Timeout accounts for: initial processing + 2s resubscription delay + reprocessing.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try {
            while (!cts.Token.IsCancellationRequested) {
                if (fixture.Handler is { HasFailed: true, HandledAfterFailure: > 0 }) break;

                await Task.Delay(100, cts.Token);
            }
        } catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) {
            // Fall through to assertions
        }

        await fixture.StopSubscription();

        await Assert.That(fixture.Handler.HasFailed).IsTrue();
        await Assert.That(fixture.Handler.HandledAfterFailure).IsGreaterThan(0);

        WriteLine(
            "Handler failed once, then processed {0} more events after resubscription",
            fixture.Handler.HandledAfterFailure
        );
    }

    async Task GenerateAndHandleCommands(int count) {
        var commands = Enumerable
            .Range(0, count)
            .Select(_ => DomainFixture.CreateImportBooking())
            .ToList();

        var service = new BookingService(fixture.EventStore);

        foreach (var cmd in commands) {
            var result = await service.Handle(cmd, default);
            result.ThrowIfError();
        }
    }
}
