using Azure.ResourceManager.ContainerInstance;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class ContainerInstancesPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var groups = context.Resource.GetContainerGroupsAsync(cancellationToken);
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting app '{ContainerGroupName}' at '{ResourceId}' (dry run)", name, group.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting app '{ContainerGroupName}' at '{ResourceId}'", name, group.Data.Id);
                    await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }
    }
}
