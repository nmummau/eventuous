// Copyright (C) Eventuous HQ OÜ.All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.RabbitMq.Producers;

/// <summary>
/// RabbitMQ Streams event producing options
/// </summary>
public record RabbitMqProduceOptions {
    /// <summary>
    /// Maximum number of events appended to a single stream in one batch
    /// </summary>
    public int MaxAppendEventsCount { get; init; } = 500;

    /// <summary>
    /// Timeout for the produce operation
    /// </summary>
    public TimeSpan? Deadline { get; init; }

    /// <summary>
    /// Default set of options
    /// </summary>
    public static RabbitMqProduceOptions Default { get; } = new();

    // Add RabbitMQ-specific options here if needed
}