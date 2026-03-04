---
title: "Azure Service Bus"
description: "Producers and subscriptions for Azure Service Bus"
sidebar:
  order: 7
---

[Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/) is a fully managed enterprise message broker with message queues and publish-subscribe topics. Eventuous supports Azure Service Bus as both a [producer](../../producers) and a [subscription](../../subscriptions/subs-concept) target using the `Eventuous.Azure.ServiceBus` package.

## Producer

The Azure Service Bus producer publishes messages to a queue or topic.

### Configuration

The producer requires an Azure `ServiceBusClient` instance registered in the DI container:

```csharp
builder.Services.AddSingleton(new ServiceBusClient(connectionString));
```

Then register the producer with its options:

```csharp
builder.Services.AddProducer<ServiceBusProducer, ServiceBusProducerOptions>(
    options => options.QueueOrTopicName = "my-topic"
);
```

Producer options:

| Option             | Description                                          | Default |
|--------------------|------------------------------------------------------|---------|
| `QueueOrTopicName` | Target queue or topic name (required)                | —       |
| `SenderOptions`    | Azure SDK `ServiceBusSenderOptions` for advanced tuning | `null`  |
| `AttributeNames`   | Customizable message attribute name mapping          | defaults |

### Produce options

When producing messages, you can supply per-message options using `ServiceBusProduceOptions`:

| Option             | Description                                              |
|--------------------|----------------------------------------------------------|
| `Subject`          | Message subject                                          |
| `To`               | Forward-to address                                       |
| `ReplyTo`          | Reply-to address                                         |
| `SessionId`        | Session id for session-enabled queues/topics             |
| `ReplyToSessionId` | Reply-to session id                                      |
| `TimeToLive`       | Message time-to-live, default is `TimeSpan.MaxValue`     |

Message metadata is mapped to Azure Service Bus application properties. The attribute names used for standard properties (message type, stream name, correlation id, etc.) can be customized via `ServiceBusMessageAttributeNames`.

## Subscriptions

Eventuous supports consuming messages from Azure Service Bus queues and topics.

### Configuration

Register a subscription using `AddSubscription`:

```csharp
builder.Services.AddSubscription<ServiceBusSubscription, ServiceBusSubscriptionOptions>(
    "PaymentsIntegration",
    b => b
        .Configure(cfg => cfg.QueueOrTopic = new Queue("payments-queue"))
        .AddEventHandler<PaymentsHandler>()
);
```

The `QueueOrTopic` property determines the source of messages. Three options are available:

| Type                   | Description                                                       |
|------------------------|-------------------------------------------------------------------|
| `Queue(name)`          | Subscribe to a queue                                              |
| `Topic(name)`          | Subscribe to a topic using the subscription id as the subscription name |
| `TopicAndSubscription(topic, subscription)` | Subscribe to a topic with an explicit subscription name |

Examples:

```csharp
// Queue subscription
cfg.QueueOrTopic = new Queue("my-queue");

// Topic subscription (uses subscription id as the subscription name)
cfg.QueueOrTopic = new Topic("my-topic");

// Topic subscription with explicit subscription name
cfg.QueueOrTopic = new TopicAndSubscription("my-topic", "my-subscription");
```

### Subscription options

| Option                    | Description                                                  |
|---------------------------|--------------------------------------------------------------|
| `QueueOrTopic`            | Queue or topic to subscribe to (required)                    |
| `ProcessorOptions`        | Azure SDK `ServiceBusProcessorOptions` for standard processing |
| `SessionProcessorOptions` | Enable session-based processing (see below)                  |
| `AttributeNames`          | Message attribute name mapping                               |
| `ErrorHandler`            | Custom error handling delegate                               |

### Session support

Azure Service Bus supports [sessions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions) for ordered message processing within a session group. To enable session-based processing, configure `SessionProcessorOptions`:

```csharp
builder.Services.AddSubscription<ServiceBusSubscription, ServiceBusSubscriptionOptions>(
    "OrderProcessing",
    b => b
        .Configure(cfg => {
            cfg.QueueOrTopic = new Queue("orders-session-queue");
            cfg.SessionProcessorOptions = new ServiceBusSessionProcessorOptions {
                MaxConcurrentSessions = 8
            };
        })
        .AddEventHandler<OrderHandler>()
);
```

When `SessionProcessorOptions` is set, the subscription uses `ServiceBusSessionProcessor` instead of the standard `ServiceBusProcessor`. This ensures messages with the same `SessionId` are processed in order.

To set the session id when producing messages, use `ServiceBusProduceOptions.SessionId`.

### Error handling

By default, errors are logged. You can override this by providing a custom `ErrorHandler` delegate on the subscription options:

```csharp
cfg.ErrorHandler = args => {
    logger.LogError(args.Exception, "Service Bus error");
    return Task.CompletedTask;
};
```
