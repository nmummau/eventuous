// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Microsoft.AspNetCore.SignalR;

namespace Eventuous.SignalR.Server;

public class SignalRSubscriptionHub(SubscriptionGateway<SignalRSubscriptionHub> gateway) : Hub {
    public Task SubscribeToStream(string stream, ulong? fromPosition)
        => gateway.SubscribeAsync(Context.ConnectionId, stream, fromPosition, Context.ConnectionAborted);

    public Task UnsubscribeFromStream(string stream)
        => gateway.UnsubscribeAsync(Context.ConnectionId, stream);

    public override Task OnDisconnectedAsync(Exception? exception)
        => gateway.RemoveConnectionAsync(Context.ConnectionId);
}
