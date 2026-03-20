// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.Subscriptions.Filters;

namespace Eventuous.SignalR.Server;

public delegate IMessageSubscription SubscriptionFactory(
    StreamName stream, ulong? fromPosition, ConsumePipe pipe, string subscriptionId
);

public class SignalRGatewayOptions {
    public required SubscriptionFactory SubscriptionFactory { get; set; }
}
