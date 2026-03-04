using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Checkpoints;
using Eventuous.Subscriptions.Context;
using Eventuous.Subscriptions.Filters;
using Eventuous.Tools;
using Shouldly;
using LoggingExtensions = Eventuous.TestHelpers.TUnit.Logging.LoggingExtensions;

namespace Eventuous.Tests.Subscriptions;

/// <summary>
/// Tests that verify the subscription properly triggers resubscription when a handler throws an exception.
/// This reproduces the issue described in GitHub issue #407 where exceptions in handlers cause the subscription
/// to silently stop without triggering the Dropped/Resubscribe flow.
/// </summary>
public class ResubscribeOnHandlerFailureTests {
    [Test]
    public async Task Should_trigger_dropped_when_handler_throws_with_throw_on_error(CancellationToken ct) {
        // Arrange
        var loggerFactory   = LoggingExtensions.GetLoggerFactory();
        var droppedTcs      = new TaskCompletionSource<(string Id, DropReason Reason, Exception? Ex)>();
        var subscribedCount = 0;

        var options = new TestSubscriptionOptions {
            SubscriptionId = "test-handler-failure",
            ThrowOnError   = true
        };

        var handler = new FailingHandler(failOnEvent: 2);
        var pipe    = new ConsumePipe().AddDefaultConsumer(handler);

        var checkpointStore = new NoOpCheckpointStore();

        var subscription = new TestPollingSubscription(
            options,
            checkpointStore,
            pipe,
            loggerFactory,
            eventCount: 5
        );

        // Act
        await subscription.Subscribe(
            id => Interlocked.Increment(ref subscribedCount),
            (id, reason, ex) => droppedTcs.TrySetResult((id, reason, ex)),
            ct
        );

        // Wait for the subscription to either drop or time out
        var completedTask = await Task.WhenAny(droppedTcs.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));

        // Assert
        if (completedTask == droppedTcs.Task) {
            var (id, reason, ex) = await droppedTcs.Task;
            id.ShouldBe("test-handler-failure");
            // Subscription should have been dropped due to error
            subscription.IsDropped.ShouldBeTrue("Subscription should be marked as dropped after handler failure");
        }
        else {
            // This is the bug: the subscription silently stopped without calling Dropped
            // Check if the subscription is still "running" but not processing events
            var handledCount = handler.HandledCount;

            Assert.Fail(
                $"Dropped was never called. Handler processed {handledCount} events before failure. " +
                $"IsRunning={subscription.IsRunning}, IsDropped={subscription.IsDropped}. "           +
                "This confirms the bug: exception in handler causes silent subscription death."
            );
        }

        // Cleanup
        await subscription.Unsubscribe(_ => { }, ct);

        // Verify checkpoint: only event #1 (position 0) was successfully acked before the failure on event #2
        var checkpoint = await checkpointStore.GetLastCheckpoint("test-handler-failure", ct);
        checkpoint.Position.ShouldBe((ulong)0, "Checkpoint should be at position 0 (only the first event was acked before failure)");
    }

    [Test]
    public async Task Should_skip_failed_event_and_advance_checkpoint_when_throw_on_error_disabled(CancellationToken ct) {
        // Arrange — ThrowOnError = false means Nack calls Ack (skip), so all events are processed
        var loggerFactory = LoggingExtensions.GetLoggerFactory();
        var completedTcs  = new TaskCompletionSource();

        var options = new TestSubscriptionOptions {
            SubscriptionId          = "test-handler-skip",
            ThrowOnError            = false,
            CheckpointCommitBatchSize = 1,
            CheckpointCommitDelayMs   = 100
        };

        var handler = new FailingHandler(failOnEvent: 2);
        var pipe    = new ConsumePipe().AddDefaultConsumer(handler);

        var checkpointStore = new NoOpCheckpointStore();

        var subscription = new TestPollingSubscription(
            options,
            checkpointStore,
            pipe,
            loggerFactory,
            eventCount: 5,
            onCompleted: () => completedTcs.TrySetResult()
        );

        // Act
        await subscription.Subscribe(
            _ => { },
            (_, _, _) => { },
            ct
        );

        // Wait for all events to be processed
        var completed = await Task.WhenAny(completedTcs.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));
        completed.ShouldBe(completedTcs.Task, "All events should be processed when ThrowOnError is false");

        // Cleanup — Finalize flushes pending checkpoint commits
        await subscription.Unsubscribe(_ => { }, ct);

        // Verify: checkpoint should have advanced past all events including the failed one (which was skipped)
        var checkpoint = await checkpointStore.GetLastCheckpoint("test-handler-skip", ct);
        checkpoint.Position.ShouldBe((ulong)4, "Checkpoint should be at position 4 (all events processed, failed one skipped)");
    }

    /// <summary>
    /// A handler that throws an exception when processing a specific event number.
    /// </summary>
    class FailingHandler(int failOnEvent) : BaseEventHandler {
        public int HandledCount;

        public override ValueTask<EventHandlingStatus> HandleEvent(IMessageConsumeContext context) {
            var count = Interlocked.Increment(ref HandledCount);

            if (count == failOnEvent)
                throw new InvalidOperationException($"Simulated handler failure on event #{count}");

            return new(EventHandlingStatus.Success);
        }
    }

    record TestSubscriptionOptions : SubscriptionWithCheckpointOptions;

    /// <summary>
    /// A minimal polling subscription that generates synthetic events and sends them
    /// through the full pipeline (including AsyncHandlingFilter).
    /// </summary>
    class TestPollingSubscription(
            TestSubscriptionOptions options,
            ICheckpointStore        checkpointStore,
            ConsumePipe             pipe,
            ILoggerFactory?         loggerFactory,
            int                     eventCount,
            Action?                 onCompleted = null
        )
        : EventSubscriptionWithCheckpoint<TestSubscriptionOptions>(
            options,
            checkpointStore,
            pipe,
            1,
            SubscriptionKind.All,
            loggerFactory,
            null,
            null
        ) {
        TaskRunner? _runner;

        protected override ValueTask Subscribe(CancellationToken cancellationToken) {
            _runner = new TaskRunner(token => PollEvents(token)).Start();

            return default;
        }

        protected override async ValueTask Unsubscribe(CancellationToken cancellationToken) {
            if (_runner == null) return;

            await _runner.Stop(cancellationToken);
            _runner.Dispose();
            _runner = null;
        }

        async Task PollEvents(CancellationToken cancellationToken) {
            var checkpoint = await GetCheckpoint(cancellationToken);
            var start = (int)(checkpoint.Position ?? 0);

            for (var i = start; i < eventCount && !cancellationToken.IsCancellationRequested; i++) {
                var context = new MessageConsumeContext(
                    Guid.NewGuid().ToString(),
                    "TestEvent",
                    "application/json",
                    "test-stream",
                    (ulong)i,
                    (ulong)i,
                    (ulong)i,
                    Sequence++,
                    DateTime.UtcNow,
                    new { EventNumber = i },
                    new Metadata(),
                    Options.SubscriptionId,
                    cancellationToken
                ) { LogContext = Log };

                await HandleInternal(context).NoContext();

                await Task.Delay(50, cancellationToken);
            }

            onCompleted?.Invoke();
        }
    }
}
