using Bookings.Payments;
using Bookings.Payments.Domain;
using Bookings.Payments.Infrastructure;
using Eventuous;
using Eventuous.RabbitMq;
using Microsoft.Extensions.Options;
using Serilog;

TypeMap.RegisterKnownEventTypes();
Logging.ConfigureLog();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// OpenTelemetry instrumentation must be added before adding Eventuous services

builder.Services.AddTelemetry();

builder.Services.AddSingleton<IRabbitMqStream, RabbitMqStream>();

builder.Services.Configure<RabbitMqConfiguration>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<IRabbitMqConfiguration>(sp =>
    sp.GetRequiredService<IOptions<RabbitMqConfiguration>>().Value
);

builder.Services.AddEventuous(builder.Configuration);

var app = builder.Build();

app.Services.AddEventuousLogs();
app.UseSwagger().UseSwaggerUI();
app.UseOpenTelemetryPrometheusScrapingEndpoint();

// Here we discover commands by their annotations
app.MapDiscoveredCommands<PaymentState>();

try {
    app.Run("http://*:5052");

    return 0;
} catch (Exception e) {
    Log.Fatal(e, "Host terminated unexpectedly");

    return 1;
} finally {
    Log.CloseAndFlush();
}