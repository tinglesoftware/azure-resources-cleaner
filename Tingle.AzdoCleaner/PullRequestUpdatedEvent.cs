using System.Text.Json.Serialization;

namespace Tingle.AzdoCleaner;

public sealed class PullRequestUpdatedEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [JsonPropertyName("resource")]
    public PullRequestResource? Resource { get; set; }
}

public class PullRequestResource
{
    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class Repository
{
    [JsonPropertyName("project")]
    public RepositoryProject? Project { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

public class RepositoryProject
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
