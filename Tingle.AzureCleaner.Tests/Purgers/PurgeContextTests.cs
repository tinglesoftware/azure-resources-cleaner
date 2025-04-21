using Azure.Core;
using Tingle.AzureCleaner.Purgers;

namespace Tingle.AzureCleaner.Tests.Purgers;

public class PurgeContextTests
{
    [Fact]
    public void MakePossibleNames_Works()
    {
        Assert.Equal(["review-app-23765", "ra-23765", "ra23765"],
            PurgeContext.MakePossibleNames([23765]));
        Assert.Equal(["review-app-23765", "ra-23765", "ra23765", "review-app-50", "ra-50", "ra50"],
            PurgeContext.MakePossibleNames([23765, 50]));
    }

    [Fact]
    public void NameMatchesExpectedFormat_Works()
    {
        var possibleNames = PurgeContext.MakePossibleNames([23765]);

        // works for all in exact format
        var modified = possibleNames;
        Assert.All(modified, pn => new PurgeContext<object>(new { }, possibleNames, false).NameMatches(pn));

        // works when prefixed
        modified = [.. possibleNames.Select(pn => $"bla:{pn}")];
        Assert.All(modified, pn => new PurgeContext<object>(new { }, possibleNames, false).NameMatches(pn));

        // works when suffixed
        modified = [.. possibleNames.Select(pn => $"{pn}:bla")];
        Assert.All(modified, pn => new PurgeContext<object>(new { }, possibleNames, false).NameMatches(pn));
    }

    [Theory]
    [InlineData("/providers/Microsoft.Web/serverfarms/fabrikam-sites-ra23765", true)] // works for AppServicePlan
    [InlineData("/providers/Microsoft.App/managedEnvironments/fabrikam-sites-ra-23765", true)] // works for ManagedEnvironment
    [InlineData("/providers/Microsoft.Sql/servers/fabrikam/databases/fabrikam-sites-ra-23765", true)] // works for Azure SQL Database
    [InlineData("/providers/Microsoft.Sql/servers/fabrikam/databases/master", false)] // skips Azure SQL Database master database
    public void NameMatchesExpectedFormat_Works_ResourceIdentifier(string suffix, bool expected)
    {
        var possibleNames = PurgeContext.MakePossibleNames([23765]);
        var prefix = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/FABRIKAM";
        var resourceId = new ResourceIdentifier($"{prefix}{suffix}");
        Assert.Equal(expected, new PurgeContext<object>(new { }, possibleNames, false).NameMatches(resourceId));
    }
}
