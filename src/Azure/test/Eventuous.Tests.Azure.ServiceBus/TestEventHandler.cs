using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;
using Hypothesist;
using Hypothesist.Builders;

namespace Eventuous.Tests.Azure.ServiceBus;

public class TestEventHandler(TimeSpan? delay = null) : BaseEventHandler {
    readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

    public int Count { get; private set; }
    readonly Observer<object> _observer = new();
    readonly List<object> _messages = new();

    public On<object> AssertThat() => Hypothesis.On(_observer);

    public override async ValueTask<EventHandlingStatus> HandleEvent(IMessageConsumeContext context) {
        await Task.Delay(_delay);
        object data = context.Message!;
        _messages.Add(data);
        await _observer.Add(data, context.CancellationToken);
        Count++;

        return EventHandlingStatus.Success;
    }

    /// <summary>
    /// Gets the messages that have been handled by this event handler.
    /// Did try and get straight from the observer, but it kept blocking
    /// </summary>
    public IEnumerable<object> Messages => _messages;
}