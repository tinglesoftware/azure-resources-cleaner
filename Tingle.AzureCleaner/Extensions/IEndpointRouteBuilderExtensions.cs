using Microsoft.AspNetCore.Mvc;
using MiniValidation;
using System.Text.Json;
using Tingle.AzureCleaner;
using Tingle.EventBus;
using SC = Tingle.AzureCleaner.AzureCleanerSerializerContext;

namespace Microsoft.AspNetCore.Builder;

internal static class IEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapWebhooksAzure(this IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/webhooks/azure", async (ILoggerFactory loggerFactory, IEventPublisher publisher, [FromBody] AzdoEvent model) =>
        {
            var logger = loggerFactory.CreateLogger("Tingle.AzureCleaner.Webhooks");
            if (!MiniValidator.TryValidate(model, out var errors)) return Results.ValidationProblem(errors);

            var type = model.EventType;
            logger.LogInformation("Received {EventType} notification {NotificationId} on subscription {SubscriptionId}",
                                  type,
                                  model.NotificationId,
                                  model.SubscriptionId?.Replace(Environment.NewLine, ""));

            if (type is AzureDevOpsEventType.GitPullRequestUpdated)
            {
                var resource = JsonSerializer.Deserialize(model.Resource, SC.Default.AzureDevOpsEventPullRequestResource)!;
                var id = resource.PullRequestId;
                var status = resource.Status;

                /*
                 * Only the PR status is considered. Adding consideration for merge status
                 * results is more combinations that may be unnecessary.
                 * For example: status = abandoned, mergeStatus = conflict
                */
                string[] targetStatuses = ["completed", "abandoned", "draft"];
                if (targetStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                {
                    var url = resource.Repository?.RemoteUrl
                           ?? resource.Repository?.Project?.Url
                           ?? throw new InvalidOperationException("RemoteUrl and Project URL should not both be null");
                    var evt = new AzdoCleanupEvent
                    {
                        Ids = [id],
                        Url = url,
                    };
                    // if the PR closes immediately after the resources are created they may not be removed
                    // adding a delay allows the changes in the cloud provider to have propagated
                    var delay = TimeSpan.FromMinutes(1);
                    await publisher.PublishAsync(@event: evt, delay: delay);
                }
                else
                {
                    logger.LogTrace("PR {PullRequestId} was updated but the status didn't match. Status '{Status}'", id, status);
                }
            }
            else
            {
                logger.LogWarning("Events of type {EventType} are not supported." +
                                  " If you wish to support them you can clone the repository or contribute a PR at https://github.com/tinglesoftware/azure-resources-cleaner",
                                  type);
            }

            return Results.Ok();
        });
    }
}
