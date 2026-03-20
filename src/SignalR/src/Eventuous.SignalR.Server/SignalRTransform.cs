// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Eventuous.Subscriptions.Context;

namespace Eventuous.SignalR.Server;

public static class SignalRTransform {
    public static RouteAndTransform<SignalRProduceOptions> Create(
        string connectionId, string stream, IEventSerializer serializer
    ) => ctx => {
        var result = serializer.SerializeEvent(ctx.Message!);
        var envelope = new StreamEventEnvelope {
            EventId        = Guid.TryParse(ctx.MessageId, out var id) ? id : Guid.NewGuid(),
            Stream         = stream,
            EventType      = ctx.MessageType,
            StreamPosition = ctx.StreamPosition,
            GlobalPosition = ctx.GlobalPosition,
            Timestamp      = ctx.Created,
            JsonPayload    = Encoding.UTF8.GetString(result.Payload),
            JsonMetadata   = ctx.Metadata is { Count: > 0 }
                ? JsonSerializer.Serialize(ctx.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value))
                : null
        };
        return ValueTask.FromResult(new[] {
            new GatewayMessage<SignalRProduceOptions>(
                new StreamName(stream), envelope, ctx.Metadata, new SignalRProduceOptions(connectionId)
            )
        });
    };
}
