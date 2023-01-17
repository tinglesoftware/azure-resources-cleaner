using Xunit;

namespace Tingle.AzdoCleaner.Tests;

public class AzdoProjectUrlTests
{
    [Theory]
    [InlineData("https://dev.azure.com/fabrikam/DefaultCollection", "https://dev.azure.com/fabrikam/DefaultCollection")]
    [InlineData("https://dev.azure.com/fabrikam/_apis/projects/DefaultCollection", "https://dev.azure.com/fabrikam/DefaultCollection")]
    [InlineData("https://dev.azure.com/fabrikam/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", "https://dev.azure.com/fabrikam/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c")]
    [InlineData("https://fabrikam.visualstudio.com/DefaultCollection", "https://fabrikam.visualstudio.com/DefaultCollection")]
    [InlineData("https://dev.azure.com/fabrikam/DefaultCollection/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", "https://dev.azure.com/fabrikam/DefaultCollection")]
    [InlineData("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", "https://dev.azure.com/fabrikam/DefaultCollection")]
    public void Creation_WithParsing_Works(string input, string projectUrl)
    {
        var url = (AzdoProjectUrl)input;
        Assert.Equal(projectUrl, url);
    }
}
