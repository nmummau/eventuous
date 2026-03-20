// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.SignalR;

public record StreamEventEnvelope {
    public required Guid EventId { get; init; }
    public required string Stream { get; init; }
    public required string EventType { get; init; }
    public required ulong StreamPosition { get; init; }
    public required ulong GlobalPosition { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string JsonPayload { get; init; }
    public string? JsonMetadata { get; init; }
}
