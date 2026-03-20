// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.SignalR;

public record StreamSubscriptionError {
    public required string Stream { get; init; }
    public required string Message { get; init; }
}
