// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;

namespace Eventuous.SignalR.Client;

public class SignalRSubscriptionClient : IAsyncDisposable {
    readonly HubConnection _connection;
    readonly SignalRSubscriptionClientOptions _options;
    readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions = new();
    readonly IDisposable _eventRegistration;
    readonly IDisposable _errorRegistration;
    bool _disposed;

    public SignalRSubscriptionClient(HubConnection connection, SignalRSubscriptionClientOptions? options = null) {
        _connection = connection;
        _options = options ?? new SignalRSubscriptionClientOptions();

        _eventRegistration = _connection.On<StreamEventEnvelope>(
            SignalRSubscriptionMethods.StreamEvent,
            OnStreamEvent
        );

        _errorRegistration = _connection.On<StreamSubscriptionError>(
            SignalRSubscriptionMethods.StreamError,
            OnStreamError
        );

        _connection.Reconnected += OnReconnected;
        _connection.Closed += OnClosed;
    }

    public async IAsyncEnumerable<StreamEventEnvelope> SubscribeAsync(
        string stream,
        ulong? fromPosition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
    ) {
        var channel = Channel.CreateUnbounded<StreamEventEnvelope>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        var state = new SubscriptionState(channel, fromPosition);
        _subscriptions[stream] = state;

        await _connection.InvokeAsync(
            SignalRSubscriptionMethods.Subscribe,
            stream,
            fromPosition,
            ct
        ).ConfigureAwait(false);

        await foreach (var envelope in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            yield return envelope;
        }

        // Cleanup when enumeration stops
        _subscriptions.TryRemove(stream, out _);
    }

    public async Task UnsubscribeAsync(string stream) {
        if (_subscriptions.TryRemove(stream, out var state)) {
            state.Channel.Writer.TryComplete();
            await _connection.InvokeAsync(
                SignalRSubscriptionMethods.Unsubscribe,
                stream
            ).ConfigureAwait(false);
        }
    }

    internal IEventSerializer GetSerializer()
        => _options.Serializer ?? DefaultEventSerializer.Instance;

    internal bool TracingEnabled => _options.EnableTracing;

    internal SubscriptionState? GetSubscriptionState(string stream)
        => _subscriptions.TryGetValue(stream, out var state) ? state : null;

    internal void RegisterSubscription(string stream, SubscriptionState state)
        => _subscriptions[stream] = state;

    internal void RemoveSubscription(string stream) {
        if (_subscriptions.TryRemove(stream, out var state)) {
            state.Channel.Writer.TryComplete();
        }
    }

    internal HubConnection Connection => _connection;

    public TypedStreamSubscription SubscribeTyped(string stream, ulong? fromPosition)
        => new TypedStreamSubscription(this, stream, fromPosition);

    void OnStreamEvent(StreamEventEnvelope envelope) {
        if (!_subscriptions.TryGetValue(envelope.Stream, out var state)) return;

        // Deduplication: skip events at or before the last seen position
        if (state.LastPosition.HasValue && envelope.StreamPosition <= state.LastPosition.Value) return;

        state.LastPosition = envelope.StreamPosition;
        state.Channel.Writer.TryWrite(envelope);
    }

    void OnStreamError(StreamSubscriptionError error) {
        if (_subscriptions.TryRemove(error.Stream, out var state)) {
            state.Channel.Writer.TryComplete(new Exception(error.Message));
        }
    }

    async Task OnReconnected(string? connectionId) {
        foreach (var (stream, state) in _subscriptions) {
            try {
                await _connection.InvokeAsync(
                    SignalRSubscriptionMethods.Subscribe,
                    stream,
                    state.LastPosition,
                    CancellationToken.None
                ).ConfigureAwait(false);
            } catch {
                // Best-effort reconnection
            }
        }
    }

    Task OnClosed(Exception? exception) {
        foreach (var (stream, state) in _subscriptions) {
            state.Channel.Writer.TryComplete(exception);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;

        _connection.Reconnected -= OnReconnected;
        _connection.Closed -= OnClosed;

        foreach (var (stream, state) in _subscriptions) {
            state.Channel.Writer.TryComplete();
            try {
                await _connection.InvokeAsync(
                    SignalRSubscriptionMethods.Unsubscribe,
                    stream
                ).ConfigureAwait(false);
            } catch {
                // Best effort
            }
        }

        _subscriptions.Clear();
        _eventRegistration.Dispose();
        _errorRegistration.Dispose();
    }

    internal class SubscriptionState(Channel<StreamEventEnvelope> channel, ulong? initialPosition) {
        public Channel<StreamEventEnvelope> Channel { get; } = channel;
        public ulong? LastPosition { get; set; } = initialPosition;
    }
}
