using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Runtime.Kubernetes.Options;

namespace OpenSandbox.Runtime.Kubernetes;

public static class KubernetesRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddKubernetesRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IKubernetes>(_ =>
        {
            var config = KubernetesClientConfiguration.IsInCluster()
                ? KubernetesClientConfiguration.InClusterConfig()
                : KubernetesClientConfiguration.BuildConfigFromConfigFile();
            return new k8s.Kubernetes(config);
        });

        services.AddHttpClient("KubernetesExecd");
        services.TryAddSingleton<ISandboxRuntime, KubernetesSandboxRuntime>();
        return services;
    }

    public static IServiceCollection AddKubernetesRuntime(
        this IServiceCollection services,
        Action<KubernetesRuntimeOptions> configure)
    {
        services.Configure(configure);
        return services.AddKubernetesRuntime();
    }
}
