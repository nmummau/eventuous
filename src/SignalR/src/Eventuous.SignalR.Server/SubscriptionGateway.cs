// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Eventuous.Subscriptions.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Eventuous.SignalR.Server;

public class SubscriptionGateway<THub> : IAsyncDisposable where THub : Hub {
    readonly IHubContext<THub>                                                                _hubContext;
    readonly SignalRProducer<THub>                                                            _producer;
    readonly SubscriptionFactory                                                              _subscriptionFactory;
    readonly IEventSerializer                                                                 _eventSerializer;
    readonly ILogger                                                                          _logger;
    readonly ConcurrentDictionary<(string ConnectionId, string Stream), SubscriptionState>   _subscriptions = new();

    public SubscriptionGateway(
        IHubContext<THub>      hubContext,
        SignalRProducer<THub>  producer,
        SignalRGatewayOptions  options,
        ILoggerFactory         loggerFactory,
        IEventSerializer?      eventSerializer = null
    ) {
        _hubContext          = hubContext;
        _producer            = producer;
        _subscriptionFactory = options.SubscriptionFactory;
        _eventSerializer     = eventSerializer ?? DefaultEventSerializer.Instance;
        _logger              = loggerFactory.CreateLogger<SubscriptionGateway<THub>>();
    }

    public async Task SubscribeAsync(string connectionId, string stream, ulong? fromPosition, CancellationToken ct = default) {
        var key = (connectionId, stream);

        // Remove existing subscription for same key
        if (_subscriptions.TryRemove(key, out var existing)) {
            await StopSubscription(existing).NoContext();
        }

        var transform      = SignalRTransform.Create(connectionId, stream, _eventSerializer);
        var handler        = GatewayHandlerFactory.Create(_producer, transform, awaitProduce: true);
        var pipe           = new ConsumePipe();
        pipe.AddDefaultConsumer(handler);

        var subscriptionId = $"signalr-{connectionId}-{stream}";
        var subscription   = _subscriptionFactory(new StreamName(stream), fromPosition, pipe, subscriptionId);

        var cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var state = new SubscriptionState(subscription, pipe, cts);

        _subscriptions[key] = state;

        // Start subscription in background
        _ = Task.Run(async () => {
            try {
                await subscription.Subscribe(
                    _ => _logger.LogDebug("Subscribed {SubscriptionId}", subscriptionId),
                    (id, reason, ex) => _logger.LogWarning(ex, "Subscription {SubscriptionId} dropped: {Reason}", id, reason),
                    cts.Token
                ).NoContext();
            } catch (OperationCanceledException) {
                // Expected on unsubscribe
            } catch (Exception ex) {
                _logger.LogError(ex, "Subscription {SubscriptionId} failed", subscriptionId);
                _subscriptions.TryRemove(key, out _);

                // Notify the client about the failure
                try {
                    var client = _hubContext.Clients.Client(connectionId);
                    await client.SendAsync(
                        SignalRSubscriptionMethods.StreamError,
                        new StreamSubscriptionError { Stream = stream, Message = ex.Message },
                        CancellationToken.None
                    ).NoContext();
                } catch (Exception notifyEx) {
                    _logger.LogDebug(notifyEx, "Failed to notify client {ConnectionId} about subscription error", connectionId);
                }
            }
        }, cts.Token);
    }

    public async Task UnsubscribeAsync(string connectionId, string stream) {
        if (_subscriptions.TryRemove((connectionId, stream), out var state)) {
            await StopSubscription(state).NoContext();
        }
    }

    public async Task RemoveConnectionAsync(string connectionId) {
        foreach (var key in _subscriptions.Keys.Where(k => k.ConnectionId == connectionId).ToList()) {
            if (_subscriptions.TryRemove(key, out var state)) {
                await StopSubscription(state).NoContext();
            }
        }
    }

    async Task StopSubscription(SubscriptionState state) {
        await state.Cts.CancelAsync();
        try {
            await state.Subscription.Unsubscribe(_ => { }, CancellationToken.None).NoContext();
        } catch (OperationCanceledException) {
            // Expected during cancellation
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error during subscription unsubscribe cleanup");
        }
        await state.Pipe.DisposeAsync().NoContext();
        state.Cts.Dispose();
    }

    public async ValueTask DisposeAsync() {
        foreach (var (_, state) in _subscriptions) {
            await StopSubscription(state).NoContext();
        }
        _subscriptions.Clear();
    }

    record SubscriptionState(IMessageSubscription Subscription, ConsumePipe Pipe, CancellationTokenSource Cts);
}
