// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.Producers.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Eventuous.SignalR.Server;

public class SignalRProducer<THub>(IHubContext<THub> hubContext)
    : BaseProducer<SignalRProduceOptions>(new ProducerTracingOptions { MessagingSystem = "signalr" })
    where THub : Hub {

    [RequiresDynamicCode("Only works with AOT when using DefaultStaticEventSerializer")]
    [RequiresUnreferencedCode("Only works with AOT when using DefaultStaticEventSerializer")]
    protected override async Task ProduceMessages(
        StreamName                   stream,
        IEnumerable<ProducedMessage> messages,
        SignalRProduceOptions?       options,
        CancellationToken            cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(options);
        var client = hubContext.Clients.Client(options.ConnectionId);

        foreach (var msg in messages) {
            await client.SendAsync(
                SignalRSubscriptionMethods.StreamEvent,
                msg.Message,
                cancellationToken
            ).NoContext();
        }
    }
}
