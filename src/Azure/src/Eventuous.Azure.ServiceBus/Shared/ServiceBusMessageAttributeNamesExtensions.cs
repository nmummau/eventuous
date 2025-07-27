namespace Eventuous.Azure.ServiceBus.Shared;

internal static class ServiceBusMessageAttributeNamesExtensions {

    /// <summary>
    /// Gets reserved names for Service Bus message attributes that are directly mapped to <see cref="ServiceBusMessage"/>.
    /// </summary>
    internal static HashSet<string> ReservedNames(this ServiceBusMessageAttributeNames attributeNames) =>
        new() {
            attributeNames.CorrelationId,
            attributeNames.ReplyTo,
            attributeNames.Subject,
            attributeNames.To,
            attributeNames.MessageId,
        };
}