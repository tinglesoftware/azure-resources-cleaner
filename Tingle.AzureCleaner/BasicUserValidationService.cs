using AspNetCore.Authentication.Basic;

namespace Tingle.AzureCleaner;

internal class BasicUserValidationService(IConfiguration configuration) : IBasicUserValidationService
{
    public Task<bool> IsValidAsync(string username, string password)
    {
        var expected = configuration.GetValue<string?>($"Authentication:ServiceHooks:Credentials:{username}");
        return Task.FromResult(string.Equals(expected, password, StringComparison.Ordinal));
    }
}
