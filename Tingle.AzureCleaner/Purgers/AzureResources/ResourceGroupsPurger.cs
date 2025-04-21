using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class ResourceGroupsPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var groups = context.Resource.GetResourceGroups();
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting resource group '{ResourceGroupName}' at '{ResourceId}' (dry run)", name, group.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting resource group '{ResourceGroupName}' at '{ResourceId}'", name, group.Data.Id);
                    await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
            }
        }
    }
}
