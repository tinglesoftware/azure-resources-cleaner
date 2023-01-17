using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tingle.AzdoCleaner;

public sealed class PullRequestUpdatedEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [Required]
    [JsonPropertyName("resource")]
    public PullRequestResource? Resource { get; set; }
}

public class PullRequestResource
{
    [Required]
    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [Required]
    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [Required]
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class Repository
{
    [Required]
    [JsonPropertyName("project")]
    public RepositoryProject? Project { get; set; }

    [Required]
    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

public class RepositoryProject
{
    [Required]
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
