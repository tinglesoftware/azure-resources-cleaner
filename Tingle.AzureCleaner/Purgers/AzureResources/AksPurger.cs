using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.ContainerService.Models;
using Azure.ResourceManager.Resources;
using k8s;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class AksPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        await PurgeNamespacesAsync(context, cancellationToken);
    }

    protected virtual async Task PurgeNamespacesAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken)
    {
        var clusters = context.Resource.GetContainerServiceManagedClustersAsync(cancellationToken);
        await foreach (var cluster in clusters)
        {
            // delete matching clusters
            var name = cluster.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting AKS cluster '{ClusterName}' at '{ResourceId}' (dry run)", name, cluster.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting AKS cluster '{ClusterName}' at '{ResourceId}'", name, cluster.Data.Id);
                    await cluster.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
                continue; // nothing more for the cluster
            }

            // skip stopped clusters
            if (cluster.Data.PowerStateCode == ContainerServiceStateCode.Stopped) continue;

            // fetch admin configuration
            var config = await cluster.GetClusterAdminConfigurationAsync(cancellationToken);
            var kubeClient = new Kubernetes(config);

            Logger.LogTrace("Looking for {Count} Kubernetes namespaces ({PossibleNamespaces}) ...",
                            context.PossibleNames.Count,
                            string.Join(",", context.PossibleNames));
            var namespaces = await kubeClient.ListNamespaceAsync(cancellationToken: cancellationToken); // using labelSelector causes problems, no idea why
            var found = namespaces.Items.Where(ns => context.NameMatches(ns.Metadata.Name)).ToList();
            if (found.Count > 0)
            {
                var names = found.Select(n => n.Metadata.Name).ToList();
                Logger.LogDebug("Found {TargetCount} Kubernetes namespaces to delete.\r\n{TargetNamespaces}", found.Count, names);
                foreach (var n in names)
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting Kubernetes namespace '{Namespace}' (dry run)", n);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting Kubernetes namespace '{Namespace}'", n);
                        await kubeClient.DeleteNamespaceAsync(name: n, cancellationToken: cancellationToken);
                    }
                }
            }
            else
            {
                Logger.LogTrace("No matching Kubernetes namespaces was found.");
            }
        }
    }
}
