using Bookings.Payments.Domain;
using Eventuous;
using Eventuous.Gateway;
using Eventuous.RabbitMq.Producers;
using Eventuous.Subscriptions.Context;
using static Bookings.Payments.Integration.IntegrationEvents;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Bookings.Payments.Integration;

public static class PaymentsGateway {
    static readonly StreamName Stream = new("PaymentsIntegration");

    public static ValueTask<GatewayMessage<RabbitMqProduceOptions>[]> Transform(IMessageConsumeContext original) {
        var result = original.Message is PaymentEvents.PaymentRecorded evt
            ? new GatewayMessage<RabbitMqProduceOptions>(
                Stream,
                new BookingPaymentRecorded(original.Stream.GetId(), evt.BookingId, evt.Amount, evt.Currency),
                new Metadata(),
                new RabbitMqProduceOptions()
            )
            : null;
        GatewayMessage<RabbitMqProduceOptions>[] gatewayMessages = result != null ? [result] : [];
        return ValueTask.FromResult(gatewayMessages);
    }
}

public static class IntegrationEvents {
    [EventType("BookingPaymentRecorded")]
    public record BookingPaymentRecorded(string PaymentId, string BookingId, float Amount, string Currency);
}