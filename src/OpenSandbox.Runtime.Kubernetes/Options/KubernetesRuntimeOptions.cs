namespace OpenSandbox.Runtime.Kubernetes.Options;

public sealed class KubernetesRuntimeOptions
{
    public const string SectionName = "KubernetesRuntime";

    public string Namespace { get; set; } = "opensandbox";

    public string CrdGroup { get; set; } = "sandbox.opensandbox.io";

    public string CrdVersion { get; set; } = "v1alpha1";

    public string CrdPlural { get; set; } = "batchsandboxes";

    public string? PoolName { get; set; }

    public string? ExecdImage { get; set; }

    public int ExecdPort { get; set; } = 44772;

    public int EgressSidecarPort { get; set; } = 18080;

    public string EndpointsAnnotationKey { get; set; } = "sandbox.opensandbox.io/endpoints";

    public int WatchTimeoutSeconds { get; set; } = 300;
}
