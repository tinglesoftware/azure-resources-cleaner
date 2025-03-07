using OpenTelemetry;
using System.Diagnostics;

namespace Tingle.AzureCleaner;

internal sealed class FilteringTraceProcessor : BaseProcessor<Activity>
{
    private static readonly string HttpClientActivitySourceName = "System.Net.Http";
    private static readonly string AzureHttpActivitySourceName = "Azure.Core.Http";

    /// <inheritdoc/>
    public override void OnStart(Activity data)
    {
        if (ShouldSkip(data))
        {
            data.IsAllDataRequested = false;
        }
    }

    /// <inheritdoc/>
    public override void OnEnd(Activity data)
    {
        if (ShouldSkip(data))
        {
            data.IsAllDataRequested = false;
        }
    }

    private static bool ShouldSkip(Activity data)
    {
        // Prevent all exporters from exporting internal activities
        if (data.Kind == ActivityKind.Internal) return true;

        // Azure SDKs create their own client span before calling the service using HttpClient
        // In this case, we would see two spans corresponding to the same operation
        // 1) created by Azure SDK 2) created by HttpClient
        // To prevent this duplication we are filtering the span from HttpClient
        // as span from Azure SDK contains all relevant information needed.
        // https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/samples/Diagnostics.md#avoiding-double-collection-of-http-activities
        // https://github.com/Azure/azure-sdk-for-net/blob/0aaf525dd6176cd5c8167f6c0934d635417ccdae/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/src/OpenTelemetryBuilderExtensions.cs#L114-L127
        var sourceName = data.Source.Name;
        var parentName = data.Parent?.Source.Name;
        if (sourceName == HttpClientActivitySourceName && parentName == AzureHttpActivitySourceName) return true;

        return false;
    }
}
