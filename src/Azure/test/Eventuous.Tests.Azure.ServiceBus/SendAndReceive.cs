using Eventuous.Azure.ServiceBus.Producers;
using Eventuous.Azure.ServiceBus.Subscriptions;
using Eventuous.Producers;

namespace Eventuous.Tests.Azure.ServiceBus;

[ClassDataSource<AzureServiceBusFixture>]
[NotInParallel]
public class SendAndReceive {
    public const string QueueName = "queue.1";
    public const string TopicName = "topic.1";
    /// <summary>
    /// This is strange. The 'subscription.1' in the emulator has a content type filter. we populate
    /// the content type but it still gets filtered out. So we use 'subscription.3' which has no filters.
    /// </summary>
    public const string SubscriptionName = "subscription.3";
    public static CancellationToken TestCancellationToken => TestContext.Current!.CancellationToken;
    private ServiceBusProducer producer = null!;
    private ServiceBusSubscription subscription = null!;
    private readonly string correlationId;
    private readonly Metadata metadata;
    private readonly TestEventHandler handler = new();
    private readonly AzureServiceBusFixture fixture;

    protected ServiceBusProducerOptions ServiceBusProducerOptions => new() {
        QueueOrTopicName = QueueName
    };

    protected ServiceBusSubscriptionOptions ServiceBusSubscriptionOptions => new() {
        QueueOrTopic = new Queue(QueueName),
        SubscriptionId = SubscriptionName
    };

    protected StreamName StreamName => new(QueueName);

    public SendAndReceive(AzureServiceBusFixture fixture) {
        correlationId = Guid.NewGuid().ToString();
        metadata = new Metadata().With(MetaTags.CorrelationId, correlationId);
        this.fixture = fixture;
    }

    [Test]
    public async Task SingleMessage() {
        await producer.Produce(StreamName, SomeEvent.Create(), metadata, cancellationToken: TestCancellationToken);

        // Assert
        await handler.AssertThat()
            .Timebox(TimeSpan.FromSeconds(1))
            .Single()
            .Match(evt => evt is SomeEvent)
            .Validate(TestCancellationToken);
    }

    [Test]
    public async Task LoadsOfMessages() {
        var count = 200;
        var events = Enumerable.Range(0, count).Select(SomeEvent.Create).ToList();
        await producer.Produce(StreamName, events, metadata, cancellationToken: TestCancellationToken);

        // Assert
        await handler.AssertThat()
            .Timebox(TimeSpan.FromSeconds(10))
            .Exactly(count)
            .Match(evt => evt is SomeEvent)
            .Validate(TestCancellationToken);

        var handledMessageIds = handler.Messages
            .OfType<SomeEvent>()
            .Select(m => m.Id)
            .Order()
            .ToList();
        await Assert.That(handledMessageIds).IsEquivalentTo(events.Select(e => e.Id));
    }

    [After(Test)]
    public async ValueTask CleanUpProducerAndSubscription() {
        await producer.StopAsync(TestCancellationToken);
        await subscription.Unsubscribe(id => { }, TestCancellationToken);
        await subscription.DisposeAsync();
        await producer.DisposeAsync();
    }

    [Before(Test)]
    public async Task StartProducerAndSubscription() {
        producer = fixture.CreateProducer(ServiceBusProducerOptions);
        subscription = fixture.CreateSubscription(ServiceBusSubscriptionOptions, handler, correlationId);

        await producer.StartAsync(TestCancellationToken);
        await subscription.Subscribe(id => { }, (id, reason, ex) => { }, TestCancellationToken);
    }
}
