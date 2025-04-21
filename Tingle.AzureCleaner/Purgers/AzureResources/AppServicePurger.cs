using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class AppServicePurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        await PurgeWebsitesAsync(context, cancellationToken);
        await PurgeStaticSitesAsync(context, cancellationToken);
    }

    protected virtual async Task PurgeWebsitesAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var sites = context.Resource.GetWebSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching slots
            var slots = site.GetWebSiteSlots().GetAllAsync(cancellationToken);
            await foreach (var slot in slots)
            {
                var slotName = slot.Data.Name;
                if (context.NameMatches(slotName))
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting slot '{SlotName}' in Website '{ResourceId}' (dry run)", slotName, site.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting slot '{SlotName}' in Website '{ResourceId}'", slotName, site.Data.Id);
                        await slot.DeleteAsync(Azure.WaitUntil.Completed,
                                               deleteMetrics: true,
                                               deleteEmptyServerFarm: false,
                                               cancellationToken: cancellationToken);
                    }
                }
            }

            // delete matching sites (either the name or the plan indicates a reviewapp)
            var name = site.Data.Name;
            var planName = site.Data.AppServicePlanId.Name;
            if (context.NameMatches(name) || context.NameMatches(planName))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting website '{WebsiteName}' in Plan '{ResourceId}' (dry run)", name, site.Data.AppServicePlanId);
                }
                else
                {
                    Logger.LogInformation("Deleting website '{WebsiteName}' in Plan '{ResourceId}'", name, site.Data.AppServicePlanId);
                    await site.DeleteAsync(Azure.WaitUntil.Completed,
                                           deleteMetrics: true,
                                           deleteEmptyServerFarm: false,
                                           cancellationToken: cancellationToken);
                }
            }
        }
    }

    protected virtual async Task PurgeStaticSitesAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var sites = context.Resource.GetStaticSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching sites
            var name = site.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting static site '{WebsiteName}' (dry run)", name);
                }
                else
                {
                    Logger.LogInformation("Deleting static site '{WebsiteName}'", name);
                    await site.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
                continue; // nothing more for the site
            }

            // As of 2022-10-25 I had not figured out how to automatically delete for Azure Repos though I know it works for GitHub Repos
            // https://github.com/Azure/static-web-apps/issues/956

            //// skip non-free tiers because they include automatic deletions
            //if (site.Data.Sku.Name != "Free") continue;

            // delete matching builds
            var builds = site.GetStaticSiteBuilds().GetAllAsync(cancellationToken);
            await foreach (var build in builds)
            {
                var buildName = build.Data.Name;
                if (context.NameMatches(buildName))
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting build '{BuildName}' in Static WebApp '{ResourceId}' (dry run)", buildName, site.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting build '{BuildName}' in Static WebApp '{ResourceId}'", buildName, site.Data.Id);
                        await build.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                    }
                }
            }
        }
    }
}
