namespace Tingle.AzureCleaner.Purgers;

public abstract class PurgeContext(IReadOnlyCollection<string> possibleNames, bool dryRun)
{
    public IReadOnlyCollection<string> PossibleNames { get; } = possibleNames;
    public bool DryRun { get; } = dryRun;
    public bool NameMatches(string name) => PossibleNames.Any(n => name.EndsWith(n) || name.StartsWith(n));
    public bool NameMatches(Azure.Core.ResourceIdentifier id) => NameMatches(id.Name);

    public static IReadOnlyCollection<string> MakePossibleNames(IList<int> ids)
        => [.. ids.SelectMany(id => new[] { $"review-app-{id}", $"ra-{id}", $"ra{id}", })];

    public PurgeContext<T> Convert<T>(T resource) => new(resource, PossibleNames, DryRun);
}

public sealed class PurgeContext<T>(T resource, IReadOnlyCollection<string> possibleNames, bool dryRun)
    : PurgeContext(possibleNames, dryRun)
{
    public T Resource { get; } = resource;
}
