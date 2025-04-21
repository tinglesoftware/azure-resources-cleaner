using Tingle.EventBus;

namespace Tingle.AzureCleaner;

internal class AzdoCleanupEvent
{
    public required int PullRequestId { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RawProjectUrl { get; init; }
}

internal class ProcessAzdoCleanupEventConsumer(AzureCleaner cleaner) : IEventConsumer<AzdoCleanupEvent>
{
    public async Task ConsumeAsync(EventContext<AzdoCleanupEvent> context, CancellationToken cancellationToken)
    {
        var evt = context.Event;
        await cleaner.HandleAsync(ids: [evt.PullRequestId],
                                  remoteUrl: evt.RemoteUrl,
                                  rawProjectUrl: evt.RawProjectUrl,
                                  cancellationToken: cancellationToken);
    }
}
