using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;

namespace Eventuous.Tests.Subscriptions.Base;

/// <summary>
/// Event handler that throws on the very first event it processes, then succeeds on all subsequent events
/// (including retries of the same event after resubscription). Used to test that subscriptions properly
/// recover after handler failures when ThrowOnError is true.
/// </summary>
public class FailOnceEventHandler : BaseEventHandler {
    int _failCount;
    int _handledAfterFailure;

    public bool HasFailed => Volatile.Read(ref _failCount) > 0;

    /// <summary>
    /// Number of events successfully processed after the failure occurred.
    /// A non-zero value proves the subscription recovered.
    /// </summary>
    public int HandledAfterFailure => Volatile.Read(ref _handledAfterFailure);

    public override ValueTask<EventHandlingStatus> HandleEvent(IMessageConsumeContext context) {
        // Fail on the very first event, but only once
        if (Interlocked.CompareExchange(ref _failCount, 1, 0) == 0) {
            throw new InvalidOperationException("Simulated handler failure on first event");
        }

        Interlocked.Increment(ref _handledAfterFailure);

        return new(EventHandlingStatus.Success);
    }
}
