using Microsoft.Extensions.Caching.Memory;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Tingle.AzureCleaner.Purgers;

public class DevOpsPurger(IMemoryCache cache, ILogger<DevOpsPurger> logger)
{
    public virtual async Task PurgeAsync(PurgeContext<DevOpsPurgeContextOptions> context, CancellationToken cancellationToken = default)
    {
        // skip if we do not have a URL
        var (projects, rawUrl) = context.Resource;
        if (rawUrl is null)
        {
            logger.LogTrace("No Azure DevOps URLs provided. Skipping ...");
            return;
        }

        // skip if we do not have a token configured
        if (rawUrl is null || !TryFindAzdoProject(projects, rawUrl, out var url, out var token))
        {
            logger.LogWarning("Project for '{Url}' is not configured or does not have a token.", rawUrl);
            return;
        }

        // at this point we have the Url and token so we can do the purging

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
                if (context.NameMatches(resource.Name))
                {
                    if (context.DryRun)
                    {
                        logger.LogInformation("Deleting resource '{EnvironmentName}/{ResourceName}' in '{ProjectUrl}' (dry run)", environment.Name, resource.Name, url);
                    }
                    else
                    {
                        logger.LogInformation("Deleting resource '{EnvironmentName}/{ResourceName}' in '{ProjectUrl}'", environment.Name, resource.Name, url);
                        await client.DeleteKubernetesResourceAsync(url.ProjectIdOrName, environment.Id, resource.Id, cancellationToken: cancellationToken);
                    }
                }
            }
        }
    }

    internal protected virtual bool TryFindAzdoProject(IReadOnlyDictionary<string, string> projects, string? rawUrl, out AzdoProjectUrl url, [NotNullWhen(true)] out string? token)
    {
        url = default;
        token = default;
        if (string.IsNullOrWhiteSpace(rawUrl)) return false;

        url = (AzdoProjectUrl)rawUrl;
        return projects.TryGetValue(url, out token);
    }

    protected virtual VssConnection CreateVssConnection(AzdoProjectUrl url, string token)
    {
        static string hash(string v) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v)));

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

public record DevOpsPurgeContextOptions(
    IReadOnlyDictionary<string, string> Projects,
    string? Url)
{
    public static IReadOnlyDictionary<string, string> MakeProjects(IReadOnlyList<string> projects)
        => projects.Select(e => e.Split(";")).ToDictionary(s => s[0], s => s[1]);
}
