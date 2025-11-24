// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.Subscriptions;

[PublicAPI]
public abstract record SubscriptionOptions {
    /// <summary>
    /// Subscription id is used to match event handlers with one subscription
    /// </summary>
    public string SubscriptionId { get; set; } = null!;

    /// <summary>
    /// Set to true if you want the subscription to fail and stop if anything goes wrong.
    /// </summary>
    public bool ThrowOnError { get; set; }
}

public abstract record SubscriptionWithCheckpointOptions : SubscriptionOptions {
    /// <summary>
    /// Checkpoint will be committed after processing this number of events. Default is 100.
    /// The <seealso cref="CheckpointCommitDelayMs"/> option will be considered as well, so the commit will happen when either condition is met.
    /// </summary>
    public int             CheckpointCommitBatchSize { get; set; } = 100;

    /// <summary>
    /// Checkpoint will be committed after this delay. Default is 5 seconds.
    /// The <seealso cref="CheckpointCommitBatchSize"/> option will be considered as well, so the commit will happen when either condition is met.
    /// </summary>
    public int             CheckpointCommitDelayMs   { get; set; } = 5000;

    /// <summary>
    /// Where to start reading events from if there's no checkpoint. Default is from the beginning.
    /// </summary>
    public InitialPosition StartFrom                 { get; set; } = InitialPosition.Earliest;
}
