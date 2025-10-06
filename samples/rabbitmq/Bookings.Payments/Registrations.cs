using Bookings.Payments.Application;
using Bookings.Payments.Domain;
using Bookings.Payments.Infrastructure;
using Eventuous.Diagnostics.OpenTelemetry;
using Eventuous.Projections.MongoDB;
using Eventuous.RabbitMq;
using Eventuous.RabbitMq.Producers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.Reliable;

namespace Bookings.Payments;

public static class Registrations {
    public static void AddEventuous(this IServiceCollection services, IConfiguration configuration) {
        // Register RabbitMQ Reliable Producer
        services.AddSingleton<Producer>(sp => {
            var rabbitConfig = sp.GetRequiredService<IRabbitMqConfiguration>();
            // Create StreamSystem (adjust config as needed)
            var streamSystemConfig = new StreamSystemConfig {
                // Set properties from rabbitConfig as needed
                // Example: UserName = rabbitConfig.UserName, Password = rabbitConfig.Password, etc.
            };
            var streamSystem = StreamSystem.Create(streamSystemConfig).GetAwaiter().GetResult();

            // Use stream name from configuration
            var streamName = "whatever"; // Adjust property name as needed

            var producerConfig = new ProducerConfig(streamSystem, streamName);
            return Producer.Create(producerConfig).GetAwaiter().GetResult();
        });

        services.AddEventStore<RabbitMqEventStore>();
        services.AddCommandService<CommandService, PaymentState>();
        services.AddSingleton(Mongo.ConfigureMongo(configuration));
        services.AddCheckpointStore<MongoCheckpointStore>();
        services.AddProducer<RabbitMqEventStoreProducer>();

        //services
        //    .AddGateway<AllStreamSubscription, AllStreamSubscriptionOptions, EventStoreProducer, EventStoreProduceOptions>(
        //        "IntegrationSubscription",
        //        PaymentsGateway.Transform
        //    );
    }

    public static void AddTelemetry(this IServiceCollection services) {
        services.AddOpenTelemetry()
            .WithMetrics(
                builder => builder
                    .AddAspNetCoreInstrumentation()
                    .AddEventuous()
                    .AddEventuousSubscriptions()
                    .AddPrometheusExporter()
            );

        services.AddOpenTelemetry()
            .WithTracing(
                builder => builder
                    .AddAspNetCoreInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddEventuousTracing()
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("payments"))
                    .SetSampler(new AlwaysOnSampler())
                    .AddZipkinExporter()
            );
    }
}
