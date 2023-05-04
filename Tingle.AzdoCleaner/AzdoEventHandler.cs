using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.ContainerInstance;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.ContainerService.Models;
using Azure.ResourceManager.PostgreSql;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Sql;
using k8s;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tingle.AzdoCleaner;

internal class AzdoEventHandler
{
    private readonly IMemoryCache cache;
    private readonly AzureDevOpsEventHandlerOptions options;
    private readonly ILogger logger;

    private readonly IReadOnlyDictionary<string, string> projects;

    public AzdoEventHandler(IMemoryCache cache, IOptions<AzureDevOpsEventHandlerOptions> options, ILogger<AzdoEventHandler> logger)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        projects = this.options.Projects.Select(e => e.Split(";")).ToDictionary(s => s[0], s => s[1]);
    }

    public virtual async Task HandleAsync(AzdoEvent model, CancellationToken cancellationToken = default)
    {
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
        else
        {
            logger.LogWarning("Events of type {EventType} are not supported." +
                              " If you wish to support them you can clone the repository or contribute a PR at https://github.com/tinglesoftware/azure-devops-cleaner",
                              type);
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

        var possibleNames = MakePossibleNames(prIds);
        if (token is not null)
        {
            await DeleteReviewAppsEnvironmentsAsync(url, token, possibleNames, cancellationToken);
        }

        logger.LogDebug("Finding azure subscriptions ...");
        var subscriptions = client.GetSubscriptions().GetAllAsync(cancellationToken);
        await foreach (var sub in subscriptions)
        {
            logger.LogDebug("Searching in subscription '{SubscriptionName} ({SubscriptionId})' ...", sub.Data.DisplayName, sub.Data.SubscriptionId);

            // resource group is deleted first to avoid repetition on dependent resources, it makes it easier
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

            if (options.AzurePostgreSql)
            {
                await DeleteAzurePostgreSqlAsync(sub, possibleNames, cancellationToken);
            }

            if (options.AzureSql)
            {
                await DeleteAzureSqlAsync(sub, possibleNames, cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureResourceGroupsAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var groups = sub.GetResourceGroups();
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                logger.LogInformation("Deleting resource group '{ResourceGroupName}' at '{ResourceId}'", name, group.Data.Id);
                await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureKubernetesNamespacesAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var clusters = sub.GetContainerServiceManagedClustersAsync(cancellationToken);
        await foreach (var cluster in clusters)
        {
            // delete matching clusters
            var name = cluster.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                logger.LogInformation("Deleting AKS cluster '{ClusterName}' at '{ResourceId}'", name, cluster.Data.Id);
                await cluster.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                continue; // nothing more for the cluster
            }

            // skip stopped clusters
            if (cluster.Data.PowerStateCode == ContainerServiceStateCode.Stopped) continue;

            // fetch admin configuration
            var config = await GetAzureKubernetesClusterAdminClientConfigurationAsync(cluster, cancellationToken);
            var kubeClient = new Kubernetes(config);

            logger.LogTrace("Looking for {Count} Kubernetes namespaces ({PossibleNamespaces}) ...",
                            possibleNames.Count,
                            string.Join(",", possibleNames));
            var namespaces = await kubeClient.ListNamespaceAsync(cancellationToken: cancellationToken); // using labelSelector causes problems, no idea why
            var found = namespaces.Items.Where(ns => NameMatchesExpectedFormat(possibleNames, ns.Metadata.Name)).ToList();
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
    protected virtual async Task DeleteAzureWebsitesAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var sites = sub.GetWebSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching slots
            var slots = site.GetWebSiteSlots().GetAllAsync(cancellationToken);
            await foreach (var slot in slots)
            {
                var slotName = slot.Data.Name;
                if (NameMatchesExpectedFormat(possibleNames, slotName))
                {
                    logger.LogInformation("Deleting slot '{SlotName}' in Website '{ResourceId}'", slotName, site.Data.Id);
                    await slot.DeleteAsync(Azure.WaitUntil.Completed,
                                           deleteMetrics: true,
                                           deleteEmptyServerFarm: false,
                                           cancellationToken: cancellationToken);
                }
            }

            // delete matching sites (either the name or the plan indicates a reviewapp)
            var name = site.Data.Name;
            var planName = site.Data.AppServicePlanId.Name;
            if (NameMatchesExpectedFormat(possibleNames, name) || NameMatchesExpectedFormat(possibleNames, planName))
            {
                //site.Data.AppServicePlanId
                logger.LogInformation("Deleting website '{WebsiteName}' in Plan '{ResourceId}'", name, site.Data.AppServicePlanId);
                await site.DeleteAsync(Azure.WaitUntil.Completed,
                                       deleteMetrics: true,
                                       deleteEmptyServerFarm: false,
                                       cancellationToken: cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureStaticWebAppsAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var sites = sub.GetStaticSitesAsync(cancellationToken);
        await foreach (var site in sites)
        {
            // delete matching sites
            var name = site.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
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
                if (NameMatchesExpectedFormat(possibleNames, buildName))
                {
                    logger.LogInformation("Deleting build '{BuildName}' in Static WebApp '{ResourceId}'", buildName, site.Data.Id);
                    await build.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }
    }
    protected virtual async Task DeleteAzureContainerAppsAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        // delete matching container apps (either the name or the environment indicates a reviewapp)
        var apps = sub.GetContainerAppsAsync(cancellationToken);
        await foreach (var app in apps)
        {
            var name = app.Data.Name;
            var envName = app.Data.EnvironmentId.Name;
            if (NameMatchesExpectedFormat(possibleNames, name) || NameMatchesExpectedFormat(possibleNames, envName))
            {
                logger.LogInformation("Deleting app '{ContainerAppName}' in Environment '{ResourceId}'", name, app.Data.ManagedEnvironmentId);
                await app.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
            }
        }

        // delete matching environments
        var envs = sub.GetContainerAppManagedEnvironmentsAsync(cancellationToken);
        await foreach (var env in envs)
        {
            var name = env.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                logger.LogInformation("Deleting environment '{EnvironmentName}' at '{ResourceId}'", name, env.Data.Id);
                await env.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzureContainerInstancesAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var groups = sub.GetContainerGroupsAsync(cancellationToken);
        await foreach (var group in groups)
        {
            var name = group.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                logger.LogInformation("Deleting app '{ContainerGroupName}' at '{ResourceId}'", name, group.Data.Id);
                await group.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
            }
        }
    }
    protected virtual async Task DeleteAzurePostgreSqlAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var servers = sub.GetPostgreSqlServersAsync(cancellationToken: cancellationToken);
        await foreach (var server in servers)
        {
            // delete matching servers
            var name = server.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                // delete databases in the server
                logger.LogInformation("Deleting databases for PostgreSQL Server '{PostgreSqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                var serverDatabases = server.GetPostgreSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
                await foreach (var database in serverDatabases)
                {
                    var databaseName = database.Data.Name;
                    if (databaseName.Equals("master")) continue;
                    logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                    await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }

                // delete the actual server
                logger.LogInformation("Deleting PostgreSQL Server '{PostgreSqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                await server.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                continue; // nothing more for the server
            }

            // delete matching databases
            var databases = server.GetPostgreSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var database in databases)
            {
                var databaseName = database.Data.Name;
                if (databaseName.Equals("master")) continue;
                if (NameMatchesExpectedFormat(possibleNames, databaseName))
                {
                    logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                    await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
            }
        }
    }
    protected virtual async Task DeleteAzureSqlAsync(SubscriptionResource sub, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
    {
        var servers = sub.GetSqlServersAsync(cancellationToken: cancellationToken);
        await foreach (var server in servers)
        {
            // delete matching servers
            var name = server.Data.Name;
            if (NameMatchesExpectedFormat(possibleNames, name))
            {
                // delete databases in the server
                logger.LogInformation("Deleting databases for SQL Server '{SqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                var serverDatabases = server.GetSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
                await foreach (var database in serverDatabases)
                {
                    var databaseName = database.Data.Name;
                    if (databaseName.Equals("master")) continue;
                    logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                    await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }

                // delete the actual server
                logger.LogInformation("Deleting SQL Server '{SqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                await server.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                continue; // nothing more for the server
            }

            // delete matching elastic pools
            var pools = server.GetElasticPools().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var pool in pools)
            {
                var poolName = pool.Data.Name;
                if (NameMatchesExpectedFormat(possibleNames, poolName))
                {
                    // delete databases in the pool
                    logger.LogInformation("Deleting databases for elastic pool '{ElasticPoolName}' at '{ResourceId}'", poolName, pool.Data.Id);
                    var poolDatabases = pool.GetDatabasesAsync(cancellationToken);
                    await foreach (var database in poolDatabases)
                    {
                        var databaseName = database.Data.Name;
                        if (databaseName.Equals("master")) continue;
                        logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                        await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }

                    // delete the actual pool
                    logger.LogInformation("Deleting elastic pool '{ElasticPoolName}' at '{ResourceId}'", poolName, pool.Data.Id);
                    await pool.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
            }

            // delete matching databases
            var databases = server.GetSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var database in databases)
            {
                var databaseName = database.Data.Name;
                if (databaseName.Equals("master")) continue;
                if (NameMatchesExpectedFormat(possibleNames, databaseName))
                {
                    logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                    await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
            }
        }
    }

    protected virtual async Task DeleteReviewAppsEnvironmentsAsync(AzdoProjectUrl url, string token, IReadOnlyCollection<string> possibleNames, CancellationToken cancellationToken)
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
                if (NameMatchesExpectedFormat(possibleNames, resource.Name))
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

    internal static IReadOnlyCollection<string> MakePossibleNames(IEnumerable<int> ids)
    {
        return ids.SelectMany(prId => new[] { $"review-app-{prId}", $"ra-{prId}", $"ra{prId}", })
                  .ToHashSet();
    }

    internal static bool NameMatchesExpectedFormat(IReadOnlyCollection<string> possibleNames, Azure.Core.ResourceIdentifier id)
        => NameMatchesExpectedFormat(possibleNames, id.Name);

    internal static bool NameMatchesExpectedFormat(IReadOnlyCollection<string> possibleNames, string name)
        => possibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n));

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

public class AzureDevOpsEventHandlerOptions
{
    public List<string> Projects { get; set; } = new();

    public bool AzureResourceGroups { get; set; } = true;
    public bool AzureKubernetes { get; set; } = true;
    public bool AzureWebsites { get; set; } = true;
    public bool AzureStaticWebApps { get; set; } = true;
    public bool AzureContainerApps { get; set; } = true;
    public bool AzureContainerInstances { get; set; } = true;
    public bool AzurePostgreSql { get; set; } = true;
    public bool AzureSql { get; set; } = true;
}
