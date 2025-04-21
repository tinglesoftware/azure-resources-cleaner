using Microsoft.Extensions.Options;
using Tingle.AzureCleaner.Purgers;

namespace Tingle.AzureCleaner;

internal class AzureCleaner(DevOpsPurger devOpsPurger, AzureResourcesPurger azureResourcesPurger, IOptions<AzureCleanerOptions> options)
{
    private readonly AzureCleanerOptions options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public virtual async Task HandleAsync(IList<int> ids,
                                          IList<string>? subscriptions = null,
                                          IReadOnlyDictionary<string, string>? projects = null,
                                          string? url = null,
                                          bool dryRun = false,
                                          CancellationToken cancellationToken = default)
    {
        var possibleNames = PurgeContext.MakePossibleNames(ids);
        var context = new PurgeContext<object>(new { }, possibleNames, dryRun);

        projects ??= DevOpsPurgeContextOptions.MakeProjects(options.AzdoProjects);
        await devOpsPurger.PurgeAsync(
            context.Convert(new DevOpsPurgeContextOptions(projects, url)),
            cancellationToken);

        subscriptions ??= options.Subscriptions;
        await azureResourcesPurger.PurgeAsync(
            context.Convert(new AzureResourcesPurgeContextOptions(subscriptions, options)),
            cancellationToken);
    }
}

public class AzureCleanerOptions
{
    public List<string> AzdoProjects { get; set; } = [];
    public List<string> Subscriptions { get; set; } = [];

    public bool AzureResourceGroups { get; set; } = true;
    public bool AzureKubernetes { get; set; } = true;
    public bool AzureAppService { get; set; } = true;
    public bool AzureContainerApps { get; set; } = true;
    public bool AzureContainerInstances { get; set; } = true;
    public bool AzureCosmosDB { get; set; } = true;
    public bool AzureMySql { get; set; } = true;
    public bool AzurePostgreSql { get; set; } = true;
    public bool AzureSql { get; set; } = true;
    public bool UserAssignedIdentities { get; set; } = true;

    public static implicit operator AzureResourcesPurgeOptions(AzureCleanerOptions options)
    {
        return new AzureResourcesPurgeOptions(
            ResourceGroups: options.AzureResourceGroups,
            Kubernetes: options.AzureKubernetes,
            AppService: options.AzureAppService,
            ContainerApps: options.AzureContainerApps,
            ContainerInstances: options.AzureContainerInstances,
            CosmosDB: options.AzureCosmosDB,
            MySql: options.AzureMySql,
            PostgreSql: options.AzurePostgreSql,
            Sql: options.AzureSql,
            UserAssignedIdentities: options.UserAssignedIdentities
        );
    }
}
