// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.SignalR;
using Eventuous.SignalR.Server;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Eventuous.Tests.SignalR;

public class SignalRProducerTests {
    [Test]
    public async Task ProduceMessages_SendsEnvelopeToCorrectConnection() {
        var hubContext  = Substitute.For<IHubContext<TestHub>>();
        var hubClients  = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<ISingleClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Client("conn-1").Returns(clientProxy);

        var producer = new SignalRProducer<TestHub>(hubContext);
        var envelope = new Eventuous.SignalR.StreamEventEnvelope {
            EventId        = Guid.NewGuid(),
            Stream         = "Test-1",
            EventType      = "TestEvent",
            StreamPosition = 0,
            GlobalPosition = 0,
            Timestamp      = DateTime.UtcNow,
            JsonPayload    = "{}"
        };

        await producer.Produce(
            new StreamName("Test-1"),
            [new ProducedMessage(envelope, new Metadata())],
            new SignalRProduceOptions("conn-1")
        );

        await clientProxy.Received(1).SendCoreAsync(
            Eventuous.SignalR.SignalRSubscriptionMethods.StreamEvent,
            Arg.Is<object?[]>(args => args.Length == 1 && args[0] is Eventuous.SignalR.StreamEventEnvelope),
            Arg.Any<CancellationToken>()
        ).ConfigureAwait(false);
    }
}

public class TestHub : Hub;
