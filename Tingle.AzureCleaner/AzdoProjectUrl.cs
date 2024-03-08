using System.Diagnostics.CodeAnalysis;

namespace Tingle.AzureCleaner;

public readonly struct AzdoProjectUrl : IEquatable<AzdoProjectUrl>
{
    private readonly Uri uri; // helps with case slash matching as compared to a plain string

    public AzdoProjectUrl(string value) : this(new Uri(value)) { }

    public AzdoProjectUrl(Uri uri)
    {
        this.uri = uri ?? throw new ArgumentNullException(nameof(uri));
        var host = uri.Host;

        var builder = new UriBuilder(uri) { UserName = null, Password = null };
        if (string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            OrganizationName = uri.AbsolutePath.Split("/")[1];
            builder.Path = OrganizationName + "/";
            ProjectIdOrName = uri.AbsolutePath.Replace("_apis/projects/", "").Split("/")[2];
        }
        else if (host.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            OrganizationName = host.Split(".")[0];
            builder.Path = string.Empty;
            ProjectIdOrName = uri.AbsolutePath.Replace("_apis/projects/", "").Split("/")[1];
        }
        else throw new ArgumentException($"Error parsing: '{uri}' into components");

        OrganizationUrl = builder.Uri.ToString();

        builder.Path += $"{ProjectIdOrName}";
        this.uri = builder.Uri;
    }

    public static AzdoProjectUrl Create(string hostname, string organizationName, string projectIdOrName)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, hostname);
        if (string.Equals(hostname, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = organizationName + "/" + projectIdOrName;
        }
        else if (hostname.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = "/" + projectIdOrName;
        }
        else throw new ArgumentException($"The hostname '{hostname}' cannot be used for creation.");

        return new(builder.Uri);
    }

    public string OrganizationName { get; }
    public string OrganizationUrl { get; }
    public string ProjectIdOrName { get; }

    public override string ToString() => uri.ToString();
    public override int GetHashCode() => uri.GetHashCode();
    public override bool Equals(object? obj) => obj is AzdoProjectUrl url && Equals(url);
    public bool Equals(AzdoProjectUrl other) => uri == other.uri;

    public static bool operator ==(AzdoProjectUrl left, AzdoProjectUrl right) => left.Equals(right);
    public static bool operator !=(AzdoProjectUrl left, AzdoProjectUrl right) => !(left == right);

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator AzdoProjectUrl?(string? value) => value is null ? null : new(value);
    public static implicit operator string(AzdoProjectUrl url) => url.ToString();
}
