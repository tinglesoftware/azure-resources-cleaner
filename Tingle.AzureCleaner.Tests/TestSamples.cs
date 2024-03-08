namespace Tingle.AzureCleaner.Tests;

internal class TestSamples
{
    public static class AzureDevOps
    {
        public static Stream GetResourceAsStream(string resourceName) 
            => typeof(TestSamples).Assembly.GetManifestResourceStream($"{typeof(TestSamples).Namespace}.Samples.{resourceName}")!;

        public static Stream GetPullRequestUpdated() => GetResourceAsStream("git.pullrequest.updated.json");
    }
}
