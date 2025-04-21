using System.Text.Json.Serialization;

namespace Tingle.AzureCleaner;

[JsonSerializable(typeof(AzdoEvent))]
[JsonSerializable(typeof(AzureDevOpsEventPullRequestResource))]
internal partial class AzureCleanerSerializerContext : JsonSerializerContext
{

}
