using System.Reflection;
using System.Runtime.ExceptionServices;
using StackExchange.Redis;

namespace Eventuous.Tests.Snapshots;

// Forwards real reads. The bound prevents the fork's pagination bug hanging a test.
public sealed class BoundedReader(IEventReader inner, int maxBackwardReads = 4) : IEventReader {
    public List<long> Positions { get; } = [];
    public Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, bool failIfNotFound, CancellationToken cancellationToken)
        => inner.ReadEvents(stream, start, count, failIfNotFound, cancellationToken);
    public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, bool failIfNotFound, CancellationToken cancellationToken) {
        Positions.Add(start.Value);
        if (Positions.Count > maxBackwardReads)
            throw new InvalidOperationException($"Backward read budget exceeded. Cursors: {string.Join(", ", Positions)}");
        return inner.ReadEventsBackwards(stream, start, count, failIfNotFound, cancellationToken);
    }
}

public sealed class FailingSnapshotStore(ISnapshotStore inner) : ISnapshotStore {
    public Task<Snapshot?> Read(StreamName streamName, CancellationToken cancellationToken = default) => inner.Read(streamName, cancellationToken);
    public Task Write(StreamName streamName, Snapshot snapshot, CancellationToken cancellationToken = default) => throw new IOException("Injected snapshot write failure after event commit");
    public Task Delete(StreamName streamName, CancellationToken cancellationToken = default) => inner.Delete(streamName, cancellationToken);
}

// This is a scheduling gate, not a fake Redis database: every call goes to Redis.
// After the revision read completes, wait for a real replacement write before
// returning that read to the production snapshot store.
public class RedisReadGate : DispatchProxy {
    public IDatabase Inner { get; set; } = null!;
    public Func<Task> ReplaceSnapshot { get; set; } = null!;
    public int Replacements { get; private set; }

    protected override object? Invoke(MethodInfo? method, object?[]? args) {
        object? result;
        try { result = method!.Invoke(Inner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
        if (method!.Name == nameof(IDatabase.HashGetAsync) && args is { Length: >= 2 }
            && args[1] is RedisValue field && field.ToString() == "revision" && result is Task<RedisValue> read)
            return ReplaceAfter(read);
        return result;
    }

    async Task<RedisValue> ReplaceAfter(Task<RedisValue> read) {
        var value = await read;
        if (Replacements++ == 0) await ReplaceSnapshot();
        return value;
    }
}
