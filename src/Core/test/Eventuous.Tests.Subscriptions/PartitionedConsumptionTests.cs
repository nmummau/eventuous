using System.Collections.Concurrent;
using Eventuous.Subscriptions;
using Eventuous.Subscriptions.Context;
using Eventuous.Subscriptions.Filters;
using Shouldly;

namespace Eventuous.Tests.Subscriptions;

public class PartitionedConsumptionTests {
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ShouldPreservePartitionOrderWhileOtherPartitionsProgress(bool customKey, bool collide) {
        var entered = Signal();
        var release = Signal();
        var handled = new ConcurrentQueue<(int Number, string? Key, long Partition)>();
        var handler = new Handler(async ctx => {
            var message = (Delivery)ctx.Message!;
            var partition = ctx.GetContext<AsyncConsumeContext>()!;
            if (message.Number == 1) {
                entered.TrySetResult();
                await release.Task.WaitAsync(Timeout);
            }
            handled.Enqueue((message.Number, partition.PartitionKey, partition.PartitionId));
        });
        await using var pipe = new ConsumePipe().AddDefaultConsumer(handler)
            .AddFilterFirst(new PartitioningFilter(2,
                customKey ? ctx => ((Delivery)ctx.Message!).Key : null,
                key => key == "independent" ? 1u : 0u));
        var first = new Delivery(1, "ordered");
        var second = new Delivery(2, collide ? "collision" : "ordered");
        var third = new Delivery(3, "independent");
        var acknowledgements = new ConcurrentQueue<int>();
        var completions = new[] { Signal(), Signal(), Signal() };

        try {
            await pipe.Send(Context(first, customKey ? "stream-a" : first.Key, completions[0], acknowledgements));
            await entered.Task.WaitAsync(Timeout);
            await pipe.Send(Context(second, customKey ? "stream-b" : second.Key, completions[1], acknowledgements));
            await pipe.Send(Context(third, customKey ? "stream-c" : third.Key, completions[2], acknowledgements));

            await completions[2].Task.WaitAsync(Timeout);
            handled.Select(x => x.Number).ShouldBe(new[] { 3 });
            completions[0].Task.IsCompleted.ShouldBeFalse();
            completions[1].Task.IsCompleted.ShouldBeFalse();
        }
        finally {
            release.TrySetResult();
        }

        await Task.WhenAll(completions.Select(x => x.Task)).WaitAsync(Timeout);
        handled.ToArray().ShouldBe(new[] { (3, (string?)"independent", 1L), (1, (string?)"ordered", 0L), (2, (string?)second.Key, 0L) });
        acknowledgements.ToArray().ShouldBe(new[] { 3, 1, 2 });
    }

    [Test]
    public async Task ShouldDrainAcceptedMessagesBeforeShutdownCompletes() {
        var entered = Signal();
        var release = Signal();
        var handled = new ConcurrentQueue<int>();
        var acknowledgements = new ConcurrentQueue<int>();
        var completions = Enumerable.Range(0, 5).Select(_ => Signal()).ToArray();
        var handler = new Handler(async ctx => {
            var message = (Delivery)ctx.Message!;
            if (message.Number == 0) {
                entered.TrySetResult();
                await release.Task.WaitAsync(Timeout);
            }
            handled.Enqueue(message.Number);
        });
        var pipe = new ConsumePipe().AddDefaultConsumer(handler).AddFilterFirst(new PartitioningFilter(2));
        Task? stopping = null;
        try {
            await pipe.Send(Context(new(0, "same-stream"), "same-stream", completions[0], acknowledgements));
            await entered.Task.WaitAsync(Timeout);
            for (var i = 1; i < completions.Length; i++) {
                await pipe.Send(Context(new(i, "same-stream"), "same-stream", completions[i], acknowledgements));
            }

            stopping = pipe.DisposeAsync().AsTask();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally {
            release.TrySetResult();
            await (stopping ?? pipe.DisposeAsync().AsTask()).WaitAsync(Timeout);
        }

        await Task.WhenAll(completions.Select(x => x.Task)).WaitAsync(Timeout);
        handled.ToArray().ShouldBe(Enumerable.Range(0, 5).ToArray());
        acknowledgements.ToArray().ShouldBe(Enumerable.Range(0, 5).ToArray());
    }

    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static AsyncConsumeContext Context(Delivery message, string stream, TaskCompletionSource completion, ConcurrentQueue<int> acknowledgements) {
        var inner = new MessageConsumeContext(message.Number.ToString(), nameof(Delivery), "application/json", stream,
            (ulong)message.Number, (ulong)message.Number, (ulong)message.Number, (ulong)message.Number,
            DateTime.UtcNow, message, null, "partition-tests", CancellationToken.None) {
            LogContext = Eventuous.Subscriptions.Logging.Logger.CreateContext("partition-tests", null)
        };
        return new(inner, _ => {
            acknowledgements.Enqueue(message.Number);
            completion.TrySetResult();
            return ValueTask.CompletedTask;
        }, (_, error) => {
            completion.TrySetException(error);
            return ValueTask.CompletedTask;
        });
    }

    public record Delivery(int Number, string Key);

    class Handler(Func<IMessageConsumeContext, Task> handle) : BaseEventHandler {
        public override async ValueTask<EventHandlingStatus> HandleEvent(IMessageConsumeContext context) {
            await handle(context);
            return EventHandlingStatus.Success;
        }
    }
}
