using Azure.ResourceManager.ContainerService.Models;
using k8s;

namespace Azure.ResourceManager.ContainerService;

public static class AzureExtensions
{
    public static async Task<KubernetesClientConfiguration> GetClusterAdminConfigurationAsync(this ContainerServiceManagedClusterResource cluster, CancellationToken cancellationToken = default)
    {
        var response = await cluster.GetClusterAdminCredentialsAsync(cancellationToken: cancellationToken);
        var credentials = response.Value;
        var kubeConfig = credentials.FindConfig("admin")
                      ?? credentials.FindConfig("clusterAdmin")
                      ?? throw new InvalidOperationException("Unable to get the cluster credentials");
        using var stream = new MemoryStream(kubeConfig.Value);
        return await KubernetesClientConfiguration.BuildConfigFromConfigFileAsync(stream);
    }

    public static ManagedClusterCredential? FindConfig(this ManagedClusterCredentials credentials, string name)
        => credentials.Kubeconfigs.SingleOrDefault(c => string.Equals(name, c.Name, StringComparison.OrdinalIgnoreCase));
}
