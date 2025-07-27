using Eventuous.Azure.ServiceBus.Producers;
using Eventuous.Azure.ServiceBus.Subscriptions;

namespace Eventuous.Tests.Azure.ServiceBus;

public class TopicAndQueueSourceAttribute : DataSourceGeneratorAttribute<AzureServiceBusFixture, ServiceBusProducerOptions, ServiceBusSubscriptionOptions> {

    public const string QueueName = "queue.1";
    public const string TopicName = "topic.1";
    /// <summary>
    /// This is strange. The 'subscription.1' in the emulator has a content type filter. we populate
    /// the content type but it still gets filtered out. So we use 'subscription.3' which has no filters.
    /// </summary>
    public const string SubscriptionName = "subscription.3";

    private ClassDataSourceAttribute<AzureServiceBusFixture> fixtureDataSource;
    public TopicAndQueueSourceAttribute() {
        fixtureDataSource = new ClassDataSourceAttribute<AzureServiceBusFixture>() {
            Shared = SharedType.PerTestSession
        };
    }
    public override IEnumerable<Func<(AzureServiceBusFixture, ServiceBusProducerOptions, ServiceBusSubscriptionOptions)>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) {
        yield return () => {
            var f = fixtureDataSource.GenerateDataSources(dataGeneratorMetadata).First()();
            return (
                        f,
                        new ServiceBusProducerOptions {
                            QueueOrTopicName = QueueName
                        },
                        new ServiceBusSubscriptionOptions {
                            QueueOrTopic = new Queue(QueueName),
                            SubscriptionId = SubscriptionName
                        }
                    );
        };
        yield return () => {
            var f = fixtureDataSource.GenerateDataSources(dataGeneratorMetadata).First()();
            return (
                        f,
                        new ServiceBusProducerOptions {
                            QueueOrTopicName = TopicName
                        },
                        new ServiceBusSubscriptionOptions {
                            QueueOrTopic = new Topic(TopicName),
                            SubscriptionId = SubscriptionName
                        }
                    );
        };
        yield return () => {
            var f = fixtureDataSource.GenerateDataSources(dataGeneratorMetadata).First()();
            return (
                        f,
                        new ServiceBusProducerOptions {
                            QueueOrTopicName = TopicName
                        },
                        new ServiceBusSubscriptionOptions {
                            QueueOrTopic = new TopicAndSubscription(TopicName, SubscriptionName),
                            SubscriptionId = "some-subscription" // Random id to show SubscriptionId is not used in this case
                        }
                    );
        };
    }
}