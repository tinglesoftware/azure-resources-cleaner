using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using MiniValidation;
using System.Text.Json;
using Tingle.AzdoCleaner;
using Tingle.EventBus;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Services.AddSerilog(builder =>
{
    builder.ConfigureSensitiveDataMasking(options =>
    {
        options.ExcludeProperties.AddRange(new[] {
            "ProjectUrl",
            "RemoteUrl",
            "ResourceId",
        });
    });
});

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddAuthentication()
                .AddBasic<BasicUserValidationService>(options => options.Realm = "AzdoCleaner");

builder.Services.AddAuthorization(options =>
{
    // By default, all incoming requests will be authorized according to the default policy.
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.AddNotificationsHandler();

// Add event bus
var selectedTransport = builder.Configuration.GetValue<EventBusTransportKind?>("EventBus:SelectedTransport");
builder.Services.AddEventBus(builder =>
{
    // Setup consumers
    builder.AddConsumer<ProcessAzdoCleanupEventConsumer>();

    // Setup transports
    var credential = new Azure.Identity.DefaultAzureCredential();
    if (selectedTransport is EventBusTransportKind.ServiceBus)
    {
        builder.AddAzureServiceBusTransport(
            options => ((AzureServiceBusTransportCredentials)options.Credentials).TokenCredential = credential);
    }
    else if (selectedTransport is EventBusTransportKind.QueueStorage)
    {
        builder.AddAzureQueueStorageTransport(
            options => ((AzureQueueStorageTransportCredentials)options.Credentials).TokenCredential = credential);
    }
    else if (selectedTransport is EventBusTransportKind.InMemory)
    {
        builder.AddInMemoryTransport();
    }
});

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/liveness", new HealthCheckOptions { Predicate = _ => false, }).AllowAnonymous();

app.MapWebhooksAzure();

await app.RunAsync();

internal enum EventBusTransportKind { InMemory, ServiceBus, QueueStorage, }

internal static class ApplicationExtensions
{
    public static WebApplicationBuilder AddNotificationsHandler(this WebApplicationBuilder builder)
    {
        builder.Services.AddNotificationsHandler(builder.Configuration.GetSection("Handler"));
        return builder;
    }

    public static IServiceCollection AddNotificationsHandler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<AzureDevOpsEventHandlerOptions>(configuration);
        services.AddSingleton<AzdoEventHandler>();

        return services;
    }

    public static IEndpointConventionBuilder MapWebhooksAzure(this IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/webhooks/azure", async (ILoggerFactory loggerFactory, IEventPublisher publisher, [FromBody] AzdoEvent model) =>
        {
            var logger = loggerFactory.CreateLogger("Tingle.AzdoCleaner.Webhooks");
            if (!MiniValidator.TryValidate(model, out var errors)) return Results.ValidationProblem(errors);

            var type = model.EventType;
            logger.LogInformation("Received {EventType} notification {NotificationId} on subscription {SubscriptionId}",
                                  type,
                                  model.NotificationId,
                                  model.SubscriptionId);

            if (type is AzureDevOpsEventType.GitPullRequestUpdated)
            {
                var resource = JsonSerializer.Deserialize<AzureDevOpsEventPullRequestResource>(model.Resource)!;
                var prId = resource.PullRequestId;
                var status = resource.Status;

                /*
                 * Only the PR status is considered. Adding consideration for merge status
                 * results is more combinations that may be unnecessary.
                 * For example: status = abandoned, mergeStatus = conflict
                */
                var targetStatuses = new[] { "completed", "abandoned", "draft", };
                if (targetStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                {
                    var rawProjectUrl = resource.Repository?.Project?.Url ?? throw new InvalidOperationException("Project URL should not be null");
                    var remoteUrl = resource.Repository?.RemoteUrl ?? throw new InvalidOperationException("RemoteUrl should not be null");
                    var evt = new AzdoCleanupEvent
                    {
                        PullRequestId = prId,
                        RemoteUrl = remoteUrl,
                        RawProjectUrl = rawProjectUrl,
                    };
                    // if the PR closes immediately after the resources are created they may not be removed
                    // adding a delay allows the changes in the cloud provider to have propagated
                    var delay = TimeSpan.FromMinutes(1);
                    await publisher.PublishAsync(@event: evt, delay: delay);
                }
                else
                {
                    logger.LogTrace("PR {PullRequestId} was updated but the status didn't match. Status '{Status}'", prId, status);
                }
            }
            else
            {
                logger.LogWarning("Events of type {EventType} are not supported." +
                                  " If you wish to support them you can clone the repository or contribute a PR at https://github.com/tinglesoftware/azure-devops-cleaner",
                                  type);
            }

            return Results.Ok();
        });
    }
}

internal class AzdoCleanupEvent
{
    public required int PullRequestId { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RawProjectUrl { get; init; }
}

internal class ProcessAzdoCleanupEventConsumer : IEventConsumer<AzdoCleanupEvent>
{
    private readonly AzdoEventHandler handler;

    public ProcessAzdoCleanupEventConsumer(AzdoEventHandler handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public async Task ConsumeAsync(EventContext<AzdoCleanupEvent> context, CancellationToken cancellationToken)
    {
        var evt = context.Event;
        await handler.HandleAsync(prId: evt.PullRequestId,
                                  remoteUrl: evt.RemoteUrl,
                                  rawProjectUrl: evt.RawProjectUrl,
                                  cancellationToken: cancellationToken);
    }
}
