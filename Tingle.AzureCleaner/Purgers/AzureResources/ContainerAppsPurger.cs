using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class ContainerAppsPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        // delete matching container apps (either the name or the environment indicates a reviewapp)
        var apps = context.Resource.GetContainerAppsAsync(cancellationToken);
        await foreach (var app in apps)
        {
            var name = app.Data.Name;
            var envName = app.Data.EnvironmentId.Name;
            if (context.NameMatches(name) || context.NameMatches(envName))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting app '{ContainerAppName}' in Environment '{ResourceId}' (dry run)", name, app.Data.ManagedEnvironmentId);
                }
                else
                {
                    Logger.LogInformation("Deleting app '{ContainerAppName}' in Environment '{ResourceId}'", name, app.Data.ManagedEnvironmentId);
                    await app.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }

        // delete matching container app jobs (either the name or the environment indicates a reviewapp)
        var jobs = context.Resource.GetContainerAppJobsAsync(cancellationToken);
        await foreach (var job in jobs)
        {
            var name = job.Data.Name;
            var envName = new Azure.Core.ResourceIdentifier(job.Data.EnvironmentId).Name;
            if (context.NameMatches(name) || context.NameMatches(envName))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting job '{ContainerAppJobName}' in Environment '{ResourceId}' (dry run)", name, job.Data.EnvironmentId);
                }
                else
                {
                    Logger.LogInformation("Deleting job '{ContainerAppJobName}' in Environment '{ResourceId}'", name, job.Data.EnvironmentId);
                    await job.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }

        // delete matching environments
        var envs = context.Resource.GetContainerAppManagedEnvironmentsAsync(cancellationToken);
        await foreach (var env in envs)
        {
            var name = env.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting environment '{EnvironmentName}' at '{ResourceId}' (dry run)", name, env.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting environment '{EnvironmentName}' at '{ResourceId}'", name, env.Data.Id);
                    await env.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
            }
        }
    }
}
