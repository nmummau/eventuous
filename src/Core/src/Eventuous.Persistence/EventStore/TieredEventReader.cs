// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Runtime.CompilerServices;

namespace Eventuous;

/// <summary>
/// Event reader that reads from both hot store (recent events) and archive store (events missing from the hot store).
/// It doesn't perform the archive itself, you need to use a connector to move events between hot and archive stores.
/// </summary>
/// <param name="hotReader">Event reader pointing to hot store</param>
/// <param name="archiveReader">Event reader pointing to archive store</param>
public class TieredEventReader(IEventReader hotReader, IEventReader archiveReader) : IEventReader {
    [RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
    [RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
    public async IAsyncEnumerable<StreamEvent> ReadEvents(StreamName streamName, StreamReadPosition start, int count, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var hotEvents = await LoadStreamEvents(hotReader, start, count).NoContext();

        var archivedEvents = hotEvents.Length == 0 || hotEvents[0].Revision > start.Value
            ? (await LoadStreamEvents(archiveReader, start, (int)hotEvents[0].Revision).NoContext())
                .Select(x => x with { FromArchive = true })
            : Enumerable.Empty<StreamEvent>();

        foreach (var evt in archivedEvents.Concat(hotEvents).Distinct(Comparer)) {
            yield return evt;
        }

        async Task<StreamEvent[]> LoadStreamEvents(IEventReader reader, StreamReadPosition startPosition, int localCount) {
            try {
                return await reader.ReadEvents(streamName, startPosition, localCount, true, cancellationToken).NoContext();
            } catch (StreamNotFound) {
                return [];
            }
        }
    }

    [RequiresDynamicCode(AttrConstants.DynamicSerializationMessage)]
    [RequiresUnreferencedCode(AttrConstants.DynamicSerializationMessage)]
    public async IAsyncEnumerable<StreamEvent> ReadEventsBackwards(StreamName streamName, StreamReadPosition start, int count, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var hotEvents = await LoadStreamEvents(hotReader, start, count).NoContext();

        var archivedEvents = hotEvents.Length == 0 || hotEvents[0].Revision > start.Value - count
            ? (await LoadStreamEvents(archiveReader, new(hotEvents[0].Revision - 1), count - hotEvents.Length).NoContext())
                .Select(x => x with { FromArchive = true })
            : Enumerable.Empty<StreamEvent>();

        foreach (var evt in hotEvents.Concat(archivedEvents).Distinct(Comparer)) {
            yield return evt;
        }

        async Task<StreamEvent[]> LoadStreamEvents(IEventReader reader, StreamReadPosition startPosition, int localCount) {
            try {
                return await reader.ReadEventsBackwards(streamName, startPosition, localCount, true, cancellationToken).NoContext();
            } catch (StreamNotFound) {
                return [];
            }
        }
    }

    static readonly StreamEventPositionComparer Comparer = new();

    class StreamEventPositionComparer : IEqualityComparer<StreamEvent> {
        public bool Equals(StreamEvent x, StreamEvent y) => x.Revision == y.Revision;

        public int GetHashCode(StreamEvent obj) => obj.Revision.GetHashCode();
    }
}
