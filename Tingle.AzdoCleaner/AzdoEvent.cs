using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tingle.AzdoCleaner;

public class AzdoEvent
{
    [Required]
    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("notificationId")]
    public int NotificationId { get; set; }

    [Required]
    [JsonPropertyName("eventType")]
    public AzureDevOpsEventType? EventType { get; set; }

    [Required]
    [JsonPropertyName("resource")]
    public JsonObject? Resource { get; set; }
}

public class AzureDevOpsEventPullRequestResource
{
    [Required]
    [JsonPropertyName("repository")]
    public AzureDevOpsEventRepository? Repository { get; set; }

    [Required]
    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [Required]
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class AzureDevOpsEventRepository
{
    [Required]
    [JsonPropertyName("project")]
    public AzureDevOpsEventRepositoryProject? Project { get; set; }

    [Required]
    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

public class AzureDevOpsEventRepositoryProject
{
    [Required]
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

[JsonConverter(typeof(JsonStringEnumMemberConverter))]
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
