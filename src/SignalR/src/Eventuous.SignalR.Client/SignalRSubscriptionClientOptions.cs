// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

namespace Eventuous.SignalR.Client;

public class SignalRSubscriptionClientOptions {
    public IEventSerializer? Serializer { get; set; }
    public bool EnableTracing { get; set; }
}
