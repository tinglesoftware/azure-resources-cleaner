using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tingle.Extensions.Primitives.Converters;

namespace Tingle.AzureCleaner;

public record AzdoEvent(
    [property: JsonPropertyName("subscriptionId")] string SubscriptionId,
    [property: JsonPropertyName("notificationId")] int NotificationId,
    [property: JsonPropertyName("eventType")] AzureDevOpsEventType EventType,
    [property: JsonPropertyName("resource")] JsonObject Resource);

public record AzureDevOpsEventPullRequestResource(
    [property: JsonPropertyName("repository")] AzureDevOpsEventRepository Repository,
    [property: JsonPropertyName("pullRequestId")] int PullRequestId,
    [property: JsonPropertyName("status")] string Status);

public record AzureDevOpsEventRepository(
    [property: JsonPropertyName("project")] AzureDevOpsEventRepositoryProject Project,
    [property: JsonPropertyName("remoteUrl")] string RemoteUrl);

public record AzureDevOpsEventRepositoryProject(
    [property: JsonPropertyName("url")] string Url);

[JsonConverter(typeof(JsonStringEnumMemberConverter<AzureDevOpsEventType>))]
public enum AzureDevOpsEventType
{
    [EnumMember(Value = "git.push")]
    GitPush,

    [EnumMember(Value = "git.pullrequest.updated")]
    GitPullRequestUpdated,

    [EnumMember(Value = "git.pullrequest.merged")]
    GitPullRequestMerged,

    [EnumMember(Value = "ms.vss-code.git-pullrequest-comment-event")]
    GitPullRequestCommentEvent,
}
