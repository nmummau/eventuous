// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.SignalR.Client;

public record StreamMeta(string Stream, ulong Position, DateTime Timestamp);
