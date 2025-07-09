using Azure.Identity;
using Azure.ResourceManager;
using Tingle.AzureCleaner.Purgers.AzureResources;

namespace Tingle.AzureCleaner.Purgers;

public class AzureResourcesPurger(ILoggerFactory loggerFactory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<AzureResourcesPurger>();

    public virtual async Task PurgeAsync(PurgeContext<AzureResourcesPurgeContextOptions> context, CancellationToken cancellationToken = default)
    {
        var (subscriptions, options) = context.Resource;
        var credential = new DefaultAzureCredential();
        var client = new ArmClient(credential);
        var purgers = new List<IAzureResourcesPurger>();

        // resource group is deleted first to avoid repetition on dependent resources, it makes it easier
        if (options.ResourceGroups) purgers.Add(new ResourceGroupsPurger(loggerFactory));
        if (options.Kubernetes) purgers.Add(new AksPurger(loggerFactory));
        if (options.AppService) purgers.Add(new AppServicePurger(loggerFactory));
        if (options.ContainerApps) purgers.Add(new ContainerAppsPurger(loggerFactory));
        if (options.ContainerInstances) purgers.Add(new ContainerInstancesPurger(loggerFactory));
        if (options.CosmosDB) purgers.Add(new CosmosDBPurger(loggerFactory));
        if (options.MySql) purgers.Add(new MySqlPurger(loggerFactory));
        if (options.PostgreSql) purgers.Add(new PostgreSqlPurger(loggerFactory));
        if (options.Sql) purgers.Add(new SqlPurger(loggerFactory));
        if (options.UserAssignedIdentities) purgers.Add(new UserAssignedIdentitiesPurger(loggerFactory));

        logger.LogDebug("Finding azure subscriptions ...");
        var subs = client.GetSubscriptions().GetAllAsync(cancellationToken);
        await foreach (var sub in subs)
        {
            // if we have a list of subscriptions to check, skip the ones not in the list
            if (subscriptions.Count > 0
                && !subscriptions.Contains(sub.Data.SubscriptionId, StringComparer.OrdinalIgnoreCase)
                && !subscriptions.Contains(sub.Data.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogDebug("Skipping subscription '{SubscriptionName}' ...", sub.Data.DisplayName); // no subscription ID for security reasons
                continue;
            }

            // create context and work through each purger
            var ctx = context.Convert(sub);
            logger.LogDebug("Searching in subscription '{SubscriptionName}' ...", sub.Data.DisplayName); // no subscription ID for security reasons
            foreach (var purger in purgers)
            {
                await purger.PurgeAsync(ctx, cancellationToken);
            }
        }
    }
}

/// <summary>Controls which types of resources can be purged.</summary>
public record AzureResourcesPurgeOptions(
    bool ResourceGroups = true,
    bool Kubernetes = true,
    bool AppService = true,
    bool ContainerApps = true,
    bool ContainerInstances = true,
    bool CosmosDB = true,
    bool MySql = true,
    bool PostgreSql = true,
    bool Sql = true,
    bool UserAssignedIdentities = true);

public record AzureResourcesPurgeContextOptions(
    IList<string> Subscriptions,
    AzureResourcesPurgeOptions Options);
