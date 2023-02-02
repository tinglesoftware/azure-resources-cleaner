using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.ContainerInstance;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.ContainerService.Models;
using Azure.ResourceManager.Resources;
using k8s;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Tingle.AzdoCleaner;

internal class PullRequestUpdatedHandler
{
    private readonly IMemoryCache cache;
    private readonly PullRequestUpdatedHandlerOptions options;
    private readonly ILogger logger;

    private readonly IReadOnlyDictionary<string, string> projects;

    public PullRequestUpdatedHandler(IMemoryCache cache, IOptions<PullRequestUpdatedHandlerOptions> options, ILogger<PullRequestUpdatedHandler> logger)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        projects = this.options.Projects.Select(e => e.Split(";")).ToDictionary(s => s[0], s => s[1]);
    }

    public virtual async Task HandleAsync(PullRequestUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        var resource = @event.Resource!;
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
            if (!TryFindProject(rawProjectUrl, out var url, out var token)
                && !TryFindProject(remoteUrl, out url, out token))
            {
                logger.LogWarning("Project for '{ProjectUrl}' or '{RemoteUrl}' does not have a token configured.", rawProjectUrl, remoteUrl);
            }

            await DeleteReviewAppResourcesAsync(url, token, new[] { prId, }, cancellationToken);
        }
        else
        {
            logger.LogTrace("PR {PullRequestId} was updated but the status didn't match. Status '{Status}'", prId, status);
        }
    }

    internal virtual bool TryFindProject(string rawUrl, out AzdoProjectUrl url, [NotNullWhen(true)] out string? token)
    {
        url = (AzdoProjectUrl)rawUrl;
        return projects.TryGetValue(url, out token);
    }

    protected virtual async Task DeleteReviewAppResourcesAsync(AzdoProjectUrl url, string? token, IEnumerable<int> prIds, CancellationToken cancellationToken = default)
    {
        var credential = new DefaultAzureCredential();
        var client = new ArmClient(credential);

        var possibleNames = prIds.SelectMany(prId => new[] { $"review-app-{prId}", $"ra-{prId}", $"ra{prId}", }).ToHashSet().ToList();
        if (token is not null)
        {
            await DeleteReviewAppsEnvironmentsAsync(url, token, possibleNames, cancellationToken);
        }

        logger.LogDebug("Finding azure subscriptions ...");
        var subscriptions = client.GetSubscriptions().GetAllAsync(cancellationToken);
        await foreach (var sub in subscriptions)
        {
            logger.LogDebug("Searching in subscription '{SubscriptionName} ({SubscriptionId})' ...", sub.Data.DisplayName, sub.Data.SubscriptionId);

            if (options.AzureResourceGroups)
            {
                await DeleteAzureResourceGroupsAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureKubernetes)
            {
                await DeleteAzureKubernetesNamespacesAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureWebsites)
            {
                await DeleteAzureWebsitesAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureStaticWebApps)
            {
                await DeleteAzureStaticWebAppsAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureContainerApps)
            {
                await DeleteAzureContainerAppsAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureContainerInstances)
            {
                await DeleteAzureContainerInstancesAsync(sub, possibleNames, cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureResourceGroupsAsync(SubscriptionResource sub, List<string> possibleNames, CancellationToken cancellationToken)
    {
        var groups = sub.GetResourceGroups();
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n)))
            {
                logger.LogInformation("Deleting resource group '{ResourceGroupName}' at '{ResourceId}'", name, group.Data.Id);
                await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureKubernetesNamespacesAsync(SubscriptionResource sub, IReadOnlyList<string> possibleNames, CancellationToken cancellationToken)
    {
        var clusters = sub.GetContainerServiceManagedClustersAsync(cancellationToken);
        await foreach (var cluster in clusters)
        {
            // skip stopped clusters
            if (cluster.Data.PowerStateCode == ContainerServiceStateCode.Stopped) continue;

            // fetch admin configuration
            var config = await GetAzureKubernetesClusterAdminClientConfigurationAsync(cluster, cancellationToken);
            var kubeClient = new Kubernetes(config);

            logger.LogTrace("Looking for {Count} Kubernetes namespaces ({PossibleNamespaces}) ...",
                            possibleNames.Count,
                            string.Join(",", possibleNames));
            var namespaces = await kubeClient.ListNamespaceAsync(cancellationToken: cancellationToken); // using labelSelector causes problems, no idea why
            var found = namespaces.Items.Where(ns => possibleNames.Any(n => ns.Metadata.Name.EndsWith(n) || ns.Metadata.Name.StartsWith(n))).ToList();
            if (found.Count > 0)
            {
                var names = found.Select(n => n.Metadata.Name).ToList();
                logger.LogDebug("Found {TargetCount} Kubernetes namespaces to delete.\r\n{TargetNamespaces}", found.Count, names);
                foreach (var n in names)
                {
                    logger.LogInformation("Deleting Kubernetes namespace '{Namespace}'", n);
                    await kubeClient.DeleteNamespaceAsync(name: n, cancellationToken: cancellationToken);
                }
            }
            else
            {
                logger.LogTrace("No matching Kubernetes namespaces was found.");
            }
        }
    }
    protected virtual async Task DeleteAzureWebsitesAsync(SubscriptionResource sub, List<string> possibleNames, CancellationToken cancellationToken)
    {
        var sites = sub.GetWebSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching sites
            var name = site.Data.Name;
            if (possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n)))
            {
                logger.LogInformation("Deleting website '{WebsiteName}' in Plan '{ResourceId}'", name, site.Data.AppServicePlanId);
                await site.DeleteAsync(Azure.WaitUntil.Completed,
                                       deleteMetrics: true,
                                       deleteEmptyServerFarm: false,
                                       cancellationToken: cancellationToken);
                continue; // nothing more for the site
            }

            // delete matching slots
            var slots = site.GetWebSiteSlots().GetAllAsync(cancellationToken);
            await foreach (var slot in slots)
            {
                var slotName = slot.Data.Name;
                if (possibleNames.Contains(slotName, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Deleting slot '{SlotName}' in Website '{ResourceId}'", slotName, site.Data.Id);
                    await slot.DeleteAsync(Azure.WaitUntil.Completed,
                                           deleteMetrics: true,
                                           deleteEmptyServerFarm: false,
                                           cancellationToken: cancellationToken);
                }
            }
        }
    }
    protected virtual async Task DeleteAzureStaticWebAppsAsync(SubscriptionResource sub, List<string> possibleNames, CancellationToken cancellationToken)
    {
        var sites = sub.GetStaticSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching sites
            var name = site.Data.Name;
            if (possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n)))
            {
                logger.LogInformation("Deleting static site '{WebsiteName}'", name);
                await site.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
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
                if (possibleNames.Contains(buildName, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Deleting build '{BuildName}' in Static WebApp '{ResourceId}'", buildName, site.Data.Id);
                    await build.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }
    }
    protected virtual async Task DeleteAzureContainerAppsAsync(SubscriptionResource sub, List<string> possibleNames, CancellationToken cancellationToken)
    {
        var apps = sub.GetContainerAppsAsync(cancellationToken);
        await foreach (var app in apps)
        {
            var name = app.Data.Name;
            if (possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n)))
            {
                logger.LogInformation("Deleting app '{ContainerAppName}' in Environment '{ResourceId}'", name, app.Data.ManagedEnvironmentId);
                await app.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureContainerInstancesAsync(SubscriptionResource sub, List<string> possibleNames, CancellationToken cancellationToken)
    {
        var groups = sub.GetContainerGroupsAsync(cancellationToken);
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n)))
            {
                logger.LogInformation("Deleting app '{ContainerGroupName}' at '{ResourceId}'", name, group.Data.Id);
                await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
            }
        }
    }

    protected virtual async Task DeleteReviewAppsEnvironmentsAsync(AzdoProjectUrl url, string token, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var connection = CreateVssConnection(url, token);

        var client = await connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);

        // iterate through all environments and resources
        var environments = await client.GetEnvironmentsAsync(url.ProjectIdOrName, cancellationToken: cancellationToken);
        foreach (var env in environments)
        {
            var environment = await client.GetEnvironmentByIdAsync(project: url.ProjectIdOrName,
                                                                   environmentId: env.Id,
                                                                   expands: EnvironmentExpands.ResourceReferences,
                                                                   cancellationToken: cancellationToken);
            logger.LogTrace("Found {ResourcesCount} resources in '{ProjectIdOrName}' and Environment '{EnvironmentName}'.\r\nResources:{ResourceNames}",
                            environment.Resources.Count,
                            url.ProjectIdOrName,
                            environment.Name,
                            string.Join(",", environment.Resources.Select(r => r.Name)));

            foreach (var resource in environment.Resources)
            {
                if (names.Contains(resource.Name, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Deleting resource '{EnvironmentName}/{ResourceName}' in '{ProjectUrl}'", environment.Name, resource.Name, url);
                    await client.DeleteKubernetesResourceAsync(url.ProjectIdOrName, environment.Id, resource.Id, cancellationToken: cancellationToken);
                }
            }
        }
    }

    protected virtual async Task<KubernetesClientConfiguration> GetAzureKubernetesClusterAdminClientConfigurationAsync(ContainerServiceManagedClusterResource cluster, CancellationToken cancellationToken)
    {
        static ManagedClusterCredential? FindConfig(ManagedClusterCredentials credentials, string name)
            => credentials.Kubeconfigs.SingleOrDefault(c => string.Equals(name, c.Name, StringComparison.OrdinalIgnoreCase));

        var response = await cluster.GetClusterAdminCredentialsAsync(cancellationToken: cancellationToken);
        var credentials = response.Value;
        var kubeConfig = FindConfig(credentials, "admin")
                         ?? FindConfig(credentials, "clusterAdmin")
                         ?? throw new InvalidOperationException("Unable to get the cluster credentials");
        using var ms = new MemoryStream(kubeConfig.Value);
        return await KubernetesClientConfiguration.BuildConfigFromConfigFileAsync(ms);
    }

    protected virtual VssConnection CreateVssConnection(AzdoProjectUrl url, string token)
    {
        static string hash(string v)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(v));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        // The cache key uses the project URL in case the token is different per project.
        // It also, uses the token to ensure a new connection if the token is updated.
        // The token is hashed to avoid exposing it just in case it is exposed.
        var cacheKey = $"vss_connections:{hash($"{url}{token}")}";
        var cached = cache.Get<VssConnection>(cacheKey);
        if (cached is not null) return cached;

        var uri = new Uri(url.OrganizationUrl);
        var creds = new VssBasicCredential(string.Empty, token);
        cached = new VssConnection(uri, creds);

        return cache.Set(cacheKey, cached, TimeSpan.FromHours(1));
    }
}

public class PullRequestUpdatedHandlerOptions
{
    public List<string> Projects { get; set; } = new();

    public bool AzureResourceGroups { get; set; } = true;
    public bool AzureKubernetes { get; set; } = true;
    public bool AzureWebsites { get; set; } = true;
    public bool AzureStaticWebApps { get; set; } = true;
    public bool AzureContainerApps { get; set; } = true;
    public bool AzureContainerInstances { get; set; } = true;
}
