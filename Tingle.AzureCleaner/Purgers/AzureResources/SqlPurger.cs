using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Sql;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class SqlPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        await PurgeStandaloneAsync(context, cancellationToken);
        await PurgeManagedInstancesAsync(context, cancellationToken);
        await PurgeManagedInstancePoolsAsync(context, cancellationToken);
    }

    protected virtual async Task PurgeStandaloneAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var servers = context.Resource.GetSqlServersAsync(cancellationToken: cancellationToken);
        await foreach (var server in servers)
        {
            // delete matching servers
            var name = server.Data.Name;
            if (context.NameMatches(name))
            {
                // delete databases in the server
                Logger.LogInformation("Deleting databases for SQL Server '{SqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                var serverDatabases = server.GetSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
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
                    Logger.LogInformation("Deleting SQL Server '{SqlServerName}' at '{ResourceId}' (dry run)", name, server.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting SQL Server '{SqlServerName}' at '{ResourceId}'", name, server.Data.Id);
                    await server.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
                continue; // nothing more for the server
            }

            // delete matching elastic pools
            var pools = server.GetElasticPools().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var pool in pools)
            {
                var poolName = pool.Data.Name;
                if (context.NameMatches(poolName))
                {
                    // delete databases in the pool
                    Logger.LogInformation("Deleting databases for elastic pool '{ElasticPoolName}' at '{ResourceId}'", poolName, pool.Data.Id);
                    var poolDatabases = pool.GetDatabasesAsync(cancellationToken);
                    await foreach (var database in poolDatabases)
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

                    // delete the actual pool
                    if (context.DryRun)
                    {
                        Logger.LogInformation("Deleting elastic pool '{ElasticPoolName}' at '{ResourceId}' (dry run)", poolName, pool.Data.Id);
                    }
                    else
                    {
                        Logger.LogInformation("Deleting elastic pool '{ElasticPoolName}' at '{ResourceId}'", poolName, pool.Data.Id);
                        await pool.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                    }
                }
            }

            // delete matching databases
            var databases = server.GetSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
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

    protected virtual async Task PurgeManagedInstancesAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken)
    {
        var instances = context.Resource.GetManagedInstancesAsync(cancellationToken: cancellationToken);
        await foreach (var instance in instances)
        {
            await PurgeManagedInstanceAsync(context, instance, cancellationToken);
        }
    }

    protected virtual async Task PurgeManagedInstancePoolsAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken)
    {
        var pools = context.Resource.GetInstancePoolsAsync(cancellationToken);
        await foreach (var pool in pools)
        {
            // delete matching pools
            var name = pool.Data.Name;
            if (context.NameMatches(name))
            {
                // delete instances in the pool
                var poolInstances = pool.GetManagedInstancesAsync(cancellationToken: cancellationToken);
                await foreach (var instance in poolInstances)
                {
                    await PurgeManagedInstanceAsync(context, instance, cancellationToken);
                }

                // delete the actual server
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting SQL Managed Instance Pool '{InstancePoolName}' at '{ResourceId}' (dry run)", name, pool.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting SQL Managed Instance Pool '{InstancePoolName}' at '{ResourceId}'", name, pool.Data.Id);
                    await pool.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                }
                continue; // nothing more for the pool
            }

            // delete matching instances
            var instances = pool.GetManagedInstancesAsync(cancellationToken: cancellationToken);
            await foreach (var instance in instances)
            {
                await PurgeManagedInstanceAsync(context, instance, cancellationToken);
            }
        }
    }

    protected virtual async Task PurgeManagedInstanceAsync(PurgeContext<SubscriptionResource> context,
                                                           ManagedInstanceResource instance,
                                                           CancellationToken cancellationToken)
    {
        // delete matching instances
        var name = instance.Data.Name;
        if (context.NameMatches(name))
        {
            // delete databases in the instance
            Logger.LogInformation("Deleting databases for SQL Managed Instance '{InstanceName}' at '{ResourceId}'", name, instance.Data.Id);
            var instanceDatabases = instance.GetManagedDatabases().GetAllAsync(cancellationToken: cancellationToken);
            await foreach (var database in instanceDatabases)
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

            // delete the actual instance
            if (context.DryRun)
            {
                Logger.LogInformation("Deleting SQL Managed Instance '{InstanceName}' at '{ResourceId}' (dry run)", name, instance.Data.Id);
            }
            else
            {
                Logger.LogInformation("Deleting SQL Managed Instance '{InstanceName}' at '{ResourceId}'", name, instance.Data.Id);
                await instance.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
            }
            return; // nothing more for the instance
        }

        // delete matching databases
        var databases = instance.GetManagedDatabases().GetAllAsync(cancellationToken: cancellationToken);
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
