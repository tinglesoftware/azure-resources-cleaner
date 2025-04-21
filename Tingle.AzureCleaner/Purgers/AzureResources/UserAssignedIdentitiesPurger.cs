using Azure.ResourceManager.ManagedServiceIdentities;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class UserAssignedIdentitiesPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var identities = context.Resource.GetUserAssignedIdentitiesAsync(cancellationToken);
        await foreach (var identity in identities)
        {
            // delete matching identities
            var name = identity.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting UserAssigned managed identity '{IdentityName}' (dry run)", name);
                }
                else
                {
                    Logger.LogInformation("Deleting UserAssigned managed identity '{IdentityName}'", name);
                    await identity.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
                continue; // nothing more for the site
            }

            // delete matching federated credentials
            var credentials = identity.GetFederatedIdentityCredentials().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var credential in credentials)
            {
                var credentialsName = credential.Data.Name;
                if (context.NameMatches(credentialsName))
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting federated credentials '{CredentialsName}' in UserAssigned managed identity '{ResourceId}' (dry run)", credentialsName, identity.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting federated credentials '{CredentialsName}' in UserAssigned managed identity '{ResourceId}'", credentialsName, identity.Data.Id);
                        await credential.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                    }
                }
            }
        }
    }
}
