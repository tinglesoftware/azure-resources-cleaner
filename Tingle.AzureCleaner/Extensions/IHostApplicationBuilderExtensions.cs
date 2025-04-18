using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Tingle.AzureCleaner;
using Tingle.AzureCleaner.OpenTelemetry;

namespace Microsoft.Extensions.DependencyInjection;

internal static class IHostApplicationBuilderExtensions
{
    /// <summary>Adds OpenTelemetry support to the application.</summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="builder"></param>
    /// <param name="isWebApp">
    /// <see langword="true"/> if the application is a web app, <see langword="false"/> otherwise.
    /// </param>
    /// <returns></returns>
    public static T AddOpenTelemetry<T>(this T builder, bool isWebApp) where T : IHostApplicationBuilder
    {
        var environment = builder.Environment;
        var configuration = builder.Configuration;
        var otel = builder.Services.AddOpenTelemetry();

        // configure the resource
        otel.ConfigureResource(resource =>
        {
            resource.AddAttributes([new("environment", environment.EnvironmentName)]);

            // add environment detectors
            resource.AddHostDetector();
            resource.AddProcessRuntimeDetector();
            if (isWebApp) resource.AddDetector(new GitResourceDetector());

            // add detectors for Azure
            resource.AddAzureAppServiceDetector();
            resource.AddAzureVMDetector();
            resource.AddAzureContainerAppsDetector();

            // add service name and version (should override any existing values)
            resource.AddService("azure-resources-cleaner", serviceVersion: VersioningHelper.ProductVersion);
        });

        // add tracing
        otel.WithTracing(tracing =>
        {
            tracing.AddSource([
                "Azure.*",
                "Tingle.EventBus",
            ]);
            tracing.AddHttpClientInstrumentation();
            tracing.AddAspNetCoreInstrumentation();

            // filter out traces we do not need
            tracing.AddProcessor(new FilteringTraceProcessor());

            // add exporter to Azure Monitor
            var aics = configuration.GetValue<string?>("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(aics))
                tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = aics);
        });

        // add metrics
        otel.WithMetrics(metrics =>
        {
            metrics.AddHttpClientInstrumentation();
            metrics.AddProcessInstrumentation();
            metrics.AddRuntimeInstrumentation();
            metrics.AddAspNetCoreInstrumentation();

            // add exporter to Azure Monitor
            var aics = configuration.GetValue<string?>("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(aics))
                metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = aics);
        });

        // add logging support
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;

            // add exporter to Azure Monitor
            var aics = configuration.GetValue<string?>("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(aics))
                options.AddAzureMonitorLogExporter(options => options.ConnectionString = aics);
        });

        return builder;
    }
}
