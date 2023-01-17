using Tingle.Extensions.Processing;

namespace Tingle.AzdoCleaner.Tests;

internal class TestSamples
{
    private const string FolderNameSamples = "Samples";

    public static class AzureDevOps
    {
        private static Stream GetAsStream(string fileName)
            => EmbeddedResourceHelper.GetResourceAsStream<TestSamples>(FolderNameSamples, fileName)!;

        public static Stream GetPullRequestUpdated() => GetAsStream("git.pullrequest.updated.json");
    }
}
