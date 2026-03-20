// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Eventuous.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

namespace Eventuous.SignalR.Client;

public class TypedStreamSubscription : IAsyncDisposable {
    readonly SignalRSubscriptionClient                              _client;
    readonly string                                                _stream;
    readonly ulong?                                                _fromPosition;
    readonly Dictionary<string, Func<object, StreamMeta, ValueTask>> _handlers = new();
    Action<StreamSubscriptionError>?                               _errorHandler;
    CancellationTokenSource?                                       _cts;
    Task?                                                          _consumeTask;
    bool                                                           _started;

    internal TypedStreamSubscription(SignalRSubscriptionClient client, string stream, ulong? fromPosition) {
        _client       = client;
        _stream       = stream;
        _fromPosition = fromPosition;
    }

    public TypedStreamSubscription On<T>(Func<T, StreamMeta, ValueTask> handler) where T : class {
        if (_started) throw new InvalidOperationException("Cannot register handlers after StartAsync has been called.");

        var eventType = TypeMap.Instance.GetTypeName<T>();
        _handlers[eventType] = (obj, meta) => handler((T)obj, meta);
        return this;
    }

    public TypedStreamSubscription OnError(Action<StreamSubscriptionError> handler) {
        _errorHandler = handler;
        return this;
    }

    public async Task StartAsync(CancellationToken ct = default) {
        if (_started) throw new InvalidOperationException("StartAsync has already been called.");
        _started = true;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var channel = Channel.CreateUnbounded<StreamEventEnvelope>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
        _client.RegisterSubscription(_stream, new SignalRSubscriptionClient.SubscriptionState(channel, _fromPosition));

        await _client.Connection.InvokeAsync(
            SignalRSubscriptionMethods.Subscribe,
            _stream,
            _fromPosition,
            _cts.Token
        ).ConfigureAwait(false);

        _consumeTask = ConsumeLoop(channel.Reader, _cts.Token);
    }

    async Task ConsumeLoop(ChannelReader<StreamEventEnvelope> reader, CancellationToken ct) {
        var serializer    = _client.GetSerializer();
        var enableTracing = _client.TracingEnabled;

        try {
            await foreach (var envelope in reader.ReadAllAsync(ct).ConfigureAwait(false)) {
                if (!_handlers.TryGetValue(envelope.EventType, out var handler)) continue;

                var payload = Encoding.UTF8.GetBytes(envelope.JsonPayload);
                var result  = serializer.DeserializeEvent(payload, envelope.EventType, "application/json");

                if (result is not DeserializationResult.SuccessfullyDeserialized deserialized) continue;

                var      meta     = new StreamMeta(envelope.Stream, envelope.StreamPosition, envelope.Timestamp);
                Activity? activity = null;

                try {
                    if (enableTracing && envelope.JsonMetadata != null) {
                        activity = StartTraceActivity(envelope.JsonMetadata);
                    }

                    await handler(deserialized.Payload, meta).ConfigureAwait(false);
                } finally {
                    activity?.Dispose();
                }
            }
        } catch (OperationCanceledException) {
            // Expected on dispose/cancellation
        } catch (ChannelClosedException) {
            // Channel completed (server error or connection closed)
        } catch (Exception ex) {
            _errorHandler?.Invoke(new StreamSubscriptionError {
                Stream  = _stream,
                Message = ex.Message
            });
        }
    }

    static Activity? StartTraceActivity(string jsonMetadata) {
        try {
            var metaDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonMetadata);
            if (metaDict == null) return null;

            var metadata      = new Metadata(metaDict);
            var tracingMeta   = metadata.GetTracingMeta();
            var parentContext = tracingMeta.ToActivityContext(isRemote: true);

            if (parentContext == null) return null;

            return EventuousDiagnostics.ActivitySource.StartActivity(
                "signalr.consume",
                ActivityKind.Consumer,
                parentContext.Value
            );
        } catch {
            return null;
        }
    }

    public async ValueTask DisposeAsync() {
        if (_cts != null) {
            await _cts.CancelAsync().ConfigureAwait(false);

            if (_consumeTask != null) {
                try { await _consumeTask.ConfigureAwait(false); } catch { /* expected */ }
            }

            _cts.Dispose();
        }

        if (_started) {
            _client.RemoveSubscription(_stream);
            try {
                await _client.Connection.InvokeAsync(
                    SignalRSubscriptionMethods.Unsubscribe,
                    _stream
                ).ConfigureAwait(false);
            } catch {
                // Best effort
            }
        }
    }
}
