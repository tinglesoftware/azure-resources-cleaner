using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public interface IAzureResourcesPurger
{
    Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default);
}

public abstract class AbstractAzureResourcesPurger : IAzureResourcesPurger
{
    protected AbstractAzureResourcesPurger(ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType());
    }

    protected ILogger Logger { get; }
    public abstract Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default);
}
