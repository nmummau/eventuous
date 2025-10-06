using System.Text.Json;
using Bookings.Application;
using Bookings.Domain;
using Bookings.Domain.Bookings;
using Bookings.Infrastructure;
using Eventuous;
using Eventuous.Diagnostics.OpenTelemetry;
using Eventuous.RabbitMq;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bookings;

public static class Registrations {
    public static void AddEventuous(this IServiceCollection services, IConfiguration configuration) {
        DefaultEventSerializer.SetDefaultSerializer(
            new DefaultEventSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureForNodaTime(DateTimeZoneProviders.Tzdb))
        );

        services.AddEventStore<RabbitMqEventStore>();
        services.AddCommandService<BookingsCommandService, BookingState>();

        services.AddSingleton<Services.IsRoomAvailable>((_, _) => new(true));
        services.AddSingleton<Services.ConvertCurrency>((from, currency) => new Money(from.Amount * 2, currency));

        services.AddSingleton(Mongo.ConfigureMongo(configuration));

        services.AddSingleton<BookingsQueryService>();
    }

    public static void AddTelemetry(this IServiceCollection services) {
        var otelEnabled = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") != null;

        services.AddOpenTelemetry()
            .WithMetrics(
                builder => {
                    builder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("bookings"))
                        .AddAspNetCoreInstrumentation()
                        .AddEventuous()
                        .AddEventuousSubscriptions()
                        .AddPrometheusExporter();
                    if (otelEnabled) builder.AddOtlpExporter();
                }
            );

        services.AddOpenTelemetry()
            .WithTracing(
                builder => {
                    builder
                        .AddAspNetCoreInstrumentation()
                        .AddGrpcClientInstrumentation()
                        .AddEventuousTracing()
                        .AddSource(typeof(DiagnosticsActivityEventSubscriber).Assembly.GetName().Name!)
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("bookings"))
                        .SetSampler(new AlwaysOnSampler());

                    if (otelEnabled)
                        builder.AddOtlpExporter();
                    else
                        builder.AddZipkinExporter();
                }
            );
    }
}
