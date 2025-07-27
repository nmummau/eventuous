using Eventuous.Azure.ServiceBus.Producers;
using Eventuous.Azure.ServiceBus.Subscriptions;
using Eventuous.Producers;

namespace Eventuous.Tests.Azure.ServiceBus;

[NotInParallel]
[TopicAndQueueSource]
public class SendAndReceive {
    public static CancellationToken TestCancellationToken => TestContext.Current!.CancellationToken;
    private ServiceBusProducer producer = null!;
    private ServiceBusSubscription subscription = null!;
    private readonly string correlationId;
    private readonly Metadata metadata;
    private readonly TestEventHandler handler = new();
    private readonly AzureServiceBusFixture fixture;

    private readonly StreamName streamName;
    private readonly ServiceBusProducerOptions serviceBusProducerOptions;
    private readonly ServiceBusSubscriptionOptions serviceBusSubscriptionOptions;

    public SendAndReceive(AzureServiceBusFixture fixture,
     ServiceBusProducerOptions producerOptions,
     ServiceBusSubscriptionOptions subscriptionOptions
     ) {
        streamName = new(producerOptions.QueueOrTopicName);
        correlationId = Guid.NewGuid().ToString();
        metadata = new Metadata().With(MetaTags.CorrelationId, correlationId);
        serviceBusProducerOptions = producerOptions;
        serviceBusSubscriptionOptions = subscriptionOptions;
        this.fixture = fixture;
    }

    [Test]
    public async Task SingleMessage() {
        await producer.Produce(streamName, SomeEvent.Create(), metadata, cancellationToken: TestCancellationToken);

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
        await producer.Produce(streamName, events, metadata, cancellationToken: TestCancellationToken);

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
        producer = fixture.CreateProducer(serviceBusProducerOptions);
        subscription = fixture.CreateSubscription(serviceBusSubscriptionOptions, handler, correlationId);

        await producer.StartAsync(TestCancellationToken);
        await subscription.Subscribe(id => { }, (id, reason, ex) => { }, TestCancellationToken);
    }
}
