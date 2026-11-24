using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RentRadar.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>OpenTelemetry (logs, metrics, traces → OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set) and a liveness check.</summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("MassTransit", "RentRadar.*"))
            .WithTracing(t => t
                .AddSource("MassTransit", "Npgsql", "RentRadar.*")
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>/health/live — the process is up; /health/ready — database, cache and message bus are reachable.</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    /// <summary>"InMemory" (single process, no broker) or "RabbitMq".</summary>
    public string Transport { get; set; } = "InMemory";
    public string Host { get; set; } = "localhost";
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public bool UseRabbitMq => Transport.Equals("RabbitMq", StringComparison.OrdinalIgnoreCase);
}

public static class MessagingExtensions
{
    /// <summary>MassTransit on RabbitMQ or the in-memory transport, chosen by <c>Messaging:Transport</c>.</summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator> register,
        Action<IBusRegistrationContext, IBusFactoryConfigurator>? endpoints = null)
    {
        var options = configuration.GetSection(MessagingOptions.Section).Get<MessagingOptions>() ?? new MessagingOptions();
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            register(x);
            if (options.UseRabbitMq)
            {
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(options.Host, options.VirtualHost, h =>
                    {
                        h.Username(options.Username);
                        h.Password(options.Password);
                    });
                    endpoints?.Invoke(ctx, cfg);
                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                x.UsingInMemory((ctx, cfg) =>
                {
                    endpoints?.Invoke(ctx, cfg);
                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });
        return services;
    }
}
