using Eventuous.Azure.ServiceBus.Producers;
namespace Eventuous.Tests.Azure.ServiceBus;

public class ConvertEventToMessage {
    private readonly Guid messageId = Guid.NewGuid();
    private readonly ServiceBusMessage message;

    public ConvertEventToMessage() {
        var builder = new ServiceBusMessageBuilder(DefaultEventSerializer.Instance, "test-stream", new(), new ServiceBusProduceOptions {
            Subject = "test-subject",
            To = "test-to",
            ReplyTo = "test-reply-to",
            TimeToLive = TimeSpan.FromMinutes(5)
        });
        message = builder.CreateServiceBusMessage(new Producers.ProducedMessage(
            new SomeEvent {
                Id = "event-id",
                Name = "Test Event"
            },
            new Metadata {
                [MetaTags.MessageId] = "12345",
                [MetaTags.CorrelationId] = "correlation-id",
                [MetaTags.CausationId] = "causation-id",
                ["AAA"] = 1111
            },
            new Metadata {
                ["BBB"] = "12345",
            },
            messageId
        ));
    }

    [Test]
    public async Task ContentType() {
        await Assert.That(message.ContentType).IsEqualTo("application/json");
    }

    [Test]
    public async Task MessageId() {
        await Assert.That(message.MessageId).IsEqualTo(messageId.ToString());
    }

    [Test]
    public async Task Subject() {
        await Assert.That(message.Subject).IsEqualTo("test-subject");
    }

    [Test]
    public async Task To() {
        await Assert.That(message.To).IsEqualTo("test-to");
    }

    [Test]
    public async Task ReplyTo() {
        await Assert.That(message.ReplyTo).IsEqualTo("test-reply-to");
    }

    [Test]
    public async Task TimeToLive() {
        await Assert.That(message.TimeToLive).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task CorrelationId() {
        await Assert.That(message.CorrelationId).IsEqualTo("correlation-id");
    }

    [Test]
    [Arguments("MessageId")]
    [Arguments("CorrelationId")]
    public async Task ApplicationPropertiesHasNoReservedAttributes(string propertyName) {
        await Assert.That(message.ApplicationProperties.ContainsKey(propertyName)).IsFalse();
    }

    [Test]
    [Arguments(MetaTags.CausationId, "causation-id")]
    [Arguments("AAA", 1111)]
    [Arguments("BBB", "12345")]
    public async Task ApplicationPropertiesHasAttributes(string propertyName, object expectedValue) {
        await Assert.That(message.ApplicationProperties[propertyName]).IsEqualTo(expectedValue);
    }
}
