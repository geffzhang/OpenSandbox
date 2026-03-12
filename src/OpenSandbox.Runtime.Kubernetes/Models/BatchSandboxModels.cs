using System.Text.Json.Serialization;

namespace OpenSandbox.Runtime.Kubernetes.Models;

internal sealed class BatchSandboxResource
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "sandbox.opensandbox.io/v1alpha1";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "BatchSandbox";

    [JsonPropertyName("metadata")]
    public BatchSandboxMetadata Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public BatchSandboxSpec Spec { get; set; } = new();

    [JsonPropertyName("status")]
    public BatchSandboxStatus? Status { get; set; }
}

internal sealed class BatchSandboxMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }
}

internal sealed class BatchSandboxSpec
{
    [JsonPropertyName("replicas")]
    public int Replicas { get; set; } = 1;

    [JsonPropertyName("poolRef")]
    public string? PoolRef { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("command")]
    public List<string>? Command { get; set; }

    [JsonPropertyName("env")]
    public List<BatchSandboxEnvVar>? Env { get; set; }

    [JsonPropertyName("resources")]
    public BatchSandboxResources? Resources { get; set; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }
}

internal sealed class BatchSandboxEnvVar
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal sealed class BatchSandboxResources
{
    [JsonPropertyName("limits")]
    public Dictionary<string, string>? Limits { get; set; }

    [JsonPropertyName("requests")]
    public Dictionary<string, string>? Requests { get; set; }
}

internal sealed class BatchSandboxStatus
{
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("podName")]
    public string? PodName { get; set; }

    [JsonPropertyName("podIP")]
    public string? PodIP { get; set; }
}
