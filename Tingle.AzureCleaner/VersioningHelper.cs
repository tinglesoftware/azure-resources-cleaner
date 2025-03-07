using System.Reflection;

namespace Tingle.AzureCleaner;

internal static class VersioningHelper
{
    // get the version from the assembly
    private static readonly Lazy<string> _productVersion = new(delegate
    {
        /*
         * Use the informational version if available because it has the git commit SHA.
         * Using the git commit SHA allows for maximum reproduction.
         * 
         * Examples:
         * 1) 1.7.1-ci.131+Branch.main.Sha.752f6cdfabb76e65d2b2cd18b3b284ef65713213
         * 2) 1.7.1-PullRequest10247.146+Branch.pull-10247-merge.Sha.bf46008b75eacacad3b7654959d38f8df4c7fcdb
         * 3) 1.7.1-fixes-2021-10-12-2.164+Branch.fixes-2021-10-12-2.Sha.bf46008b75eacacad3b7654959d38f8df4c7fcdb
         * 4) 1.9.3+Branch.migration-to-bedrock.Sha.ed9934bab03eaca1dfcef2c212372f1e6820418e
         * 
         * When not available, use the usual assembly version
         */
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr is null ? assembly.GetName().Version!.ToString() : attr.InformationalVersion;
    });

    public static string ProductVersion => _productVersion.Value;
}
