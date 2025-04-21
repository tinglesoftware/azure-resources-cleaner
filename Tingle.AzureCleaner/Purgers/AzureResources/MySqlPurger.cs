using Azure.ResourceManager.MySql;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class MySqlPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        await PurgeLegacyAsync(context, cancellationToken);
        await PurgeFlexibleAsync(context, cancellationToken);
    }

    protected virtual async Task PurgeLegacyAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken)
    {
        var servers = context.Resource.GetMySqlServersAsync(cancellationToken: cancellationToken);
        await foreach (var server in servers)
        {
            // delete matching servers
            var name = server.Data.Name;
            if (context.NameMatches(name))
            {
                // delete databases in the server
                Logger.LogInformation("Deleting databases for MySQL Server '{MySqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                var serverDatabases = server.GetMySqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
                await foreach (var database in serverDatabases)
                {
                    var databaseName = database.Data.Name;
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                        await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }
                }

                // delete the actual server
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting MySQL Server '{MySqlServerName}' at '{ResourceId}' (dry run)", name, server.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting MySQL Server '{MySqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                    await server.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
                continue; // nothing more for the server
            }

            // delete matching databases
            var databases = server.GetMySqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var database in databases)
            {
                var databaseName = database.Data.Name;
                if (context.NameMatches(databaseName))
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                        await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }
                }
            }
        }
    }

    protected virtual async Task PurgeFlexibleAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken)
    {
        var servers = context.Resource.GetMySqlFlexibleServersAsync(cancellationToken: cancellationToken);
        await foreach (var server in servers)
        {
            // delete matching servers
            var name = server.Data.Name;
            if (context.NameMatches(name))
            {
                // delete databases in the server
                Logger.LogInformation("Deleting databases for MySQL Server '{MySqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                var serverDatabases = server.GetMySqlFlexibleServerDatabases().GetAllAsync(cancellationToken: cancellationToken);
                await foreach (var database in serverDatabases)
                {
                    var databaseName = database.Data.Name;
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                        await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }
                }

                // delete the actual server
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting MySQL Server '{MySqlServerName}' at '{ResourceId}' (dry run)", name, server.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting MySQL Server '{MySqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                    await server.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
                continue; // nothing more for the server
            }

            // delete matching databases
            var databases = server.GetMySqlFlexibleServerDatabases().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var database in databases)
            {
                var databaseName = database.Data.Name;
                if (context.NameMatches(databaseName))
                {
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                        await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }
                }
            }
        }
    }
}
