using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSandbox.Abstractions;
using OpenSandbox.Abstractions.Contracts;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Runtime.Kubernetes.Models;
using OpenSandbox.Runtime.Kubernetes.Options;

namespace OpenSandbox.Runtime.Kubernetes;

public sealed class KubernetesSandboxRuntime : ISandboxRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IKubernetes _kubernetesClient;
    private readonly KubernetesRuntimeOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KubernetesSandboxRuntime> _logger;

    public KubernetesSandboxRuntime(
        IKubernetes kubernetesClient,
        IOptions<KubernetesRuntimeOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<KubernetesSandboxRuntime> logger)
    {
        _kubernetesClient = kubernetesClient;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> CreateAsync(SandboxRecord record, CancellationToken cancellationToken)
    {
        var resourceName = $"sandbox-{record.Id}";

        var labels = new Dictionary<string, string>
        {
            ["opensandbox.managed"] = "true",
            ["opensandbox.id"] = record.Id
        };

        if (record.Metadata != null)
        {
            foreach (var item in record.Metadata)
            {
                labels[$"opensandbox.meta.{item.Key}"] = item.Value;
            }
        }

        var resource = new BatchSandboxResource
        {
            ApiVersion = $"{_options.CrdGroup}/{_options.CrdVersion}",
            Kind = "BatchSandbox",
            Metadata = new BatchSandboxMetadata
            {
                Name = resourceName,
                Namespace = _options.Namespace,
                Labels = labels
            },
            Spec = new BatchSandboxSpec
            {
                Replicas = 1,
                PoolRef = _options.PoolName,
                Image = record.Image,
                Command = record.Entrypoint.Count > 0 ? record.Entrypoint : null,
                TimeoutSeconds = record.TimeoutSeconds > 0 ? record.TimeoutSeconds : null
            }
        };

        if (record.Env != null && record.Env.Count > 0)
        {
            resource.Spec.Env = record.Env
                .Select(kv => new BatchSandboxEnvVar { Name = kv.Key, Value = kv.Value })
                .ToList();
        }

        if (record.ResourceLimits != null)
        {
            var limits = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(record.ResourceLimits.Cpu))
            {
                limits["cpu"] = record.ResourceLimits.Cpu;
            }

            if (!string.IsNullOrWhiteSpace(record.ResourceLimits.Memory))
            {
                limits["memory"] = record.ResourceLimits.Memory;
            }

            if (limits.Count > 0)
            {
                resource.Spec.Resources = new BatchSandboxResources
                {
                    Limits = limits,
                    Requests = limits
                };
            }
        }

        _logger.LogInformation("Creating BatchSandbox CRD {ResourceName} in namespace {Namespace}.",
            resourceName, _options.Namespace);

        await _kubernetesClient.CustomObjects.CreateNamespacedCustomObjectAsync(
            body: resource,
            group: _options.CrdGroup,
            version: _options.CrdVersion,
            namespaceParameter: _options.Namespace,
            plural: _options.CrdPlural,
            cancellationToken: cancellationToken);

        return resourceName;
    }

    public async Task DeleteAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            await _kubernetesClient.CustomObjects.DeleteNamespacedCustomObjectAsync(
                group: _options.CrdGroup,
                version: _options.CrdVersion,
                namespaceParameter: _options.Namespace,
                plural: _options.CrdPlural,
                name: containerName,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted BatchSandbox CRD {ResourceName}.", containerName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("BatchSandbox CRD {ResourceName} not found during deletion.", containerName);
        }
    }

    public Task PauseAsync(string containerName, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Pause is not supported for Kubernetes BatchSandbox CRDs. Resource: {ResourceName}", containerName);
        throw new NotSupportedException("Pause is not supported for Kubernetes-based sandboxes.");
    }

    public Task ResumeAsync(string containerName, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Resume is not supported for Kubernetes BatchSandbox CRDs. Resource: {ResourceName}", containerName);
        throw new NotSupportedException("Resume is not supported for Kubernetes-based sandboxes.");
    }

    public async Task<SandboxRuntimeState?> InspectAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _kubernetesClient.CustomObjects.GetNamespacedCustomObjectAsync(
                group: _options.CrdGroup,
                version: _options.CrdVersion,
                namespaceParameter: _options.Namespace,
                plural: _options.CrdPlural,
                name: containerName,
                cancellationToken: cancellationToken);

            var json = JsonSerializer.Serialize(result, JsonOptions);
            var resource = JsonSerializer.Deserialize<BatchSandboxResource>(json, JsonOptions);
            if (resource == null)
            {
                return null;
            }

            return new SandboxRuntimeState
            {
                State = MapPhaseToState(resource.Status?.Phase),
                Reason = resource.Status?.Reason ?? resource.Status?.Phase,
                Message = resource.Status?.Message,
                ContainerId = resource.Status?.PodName
            };
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<int?> GetPublishedPortAsync(string containerName, int containerPort, CancellationToken cancellationToken)
    {
        var endpointIp = await GetSandboxEndpointIpAsync(containerName, cancellationToken);
        if (string.IsNullOrWhiteSpace(endpointIp))
        {
            return null;
        }

        // In Kubernetes, the sandbox pod IP is directly accessible within the cluster.
        // The container port is not remapped like Docker published ports.
        // Return the containerPort itself to indicate the port is available.
        return containerPort;
    }

    public async Task<SandboxRuntimeUsage?> GetUsageAsync(string containerName, CancellationToken cancellationToken)
    {
        var state = await InspectAsync(containerName, cancellationToken);
        if (state == null || !string.Equals(state.State, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Resource usage metrics are typically obtained via the Kubernetes Metrics API.
        // This returns a basic placeholder when the sandbox is running.
        return new SandboxRuntimeUsage
        {
            CollectedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<SandboxCommandResult?> ExecuteAsync(string containerName, string command, CancellationToken cancellationToken)
    {
        var endpointIp = await GetSandboxEndpointIpAsync(containerName, cancellationToken);
        if (string.IsNullOrWhiteSpace(endpointIp))
        {
            return null;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient("KubernetesExecd");
            var requestBody = JsonSerializer.Serialize(new { command }, JsonOptions);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                $"http://{endpointIp}:{_options.ExecdPort}/exec",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Execd command failed for {ResourceName}: {StatusCode} {Error}",
                    containerName, response.StatusCode, errorBody);
                return new SandboxCommandResult
                {
                    ExitCode = (int)response.StatusCode,
                    StdErr = errorBody
                };
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<SandboxCommandResult>(responseBody, JsonOptions);
            return result ?? new SandboxCommandResult { ExitCode = -1, StdErr = "Failed to parse execd response." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command via execd for {ResourceName}.", containerName);
            return new SandboxCommandResult
            {
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    public async Task RunTerminalSessionAsync(string containerName, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var endpointIp = await GetSandboxEndpointIpAsync(containerName, cancellationToken);
        if (string.IsNullOrWhiteSpace(endpointIp))
        {
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable,
                    "Sandbox endpoint not available.", CancellationToken.None);
            }

            return;
        }

        using var clientWs = new ClientWebSocket();
        var wsUri = new Uri($"ws://{endpointIp}:{_options.ExecdPort}/terminal/ws");

        try
        {
            await clientWs.ConnectAsync(wsUri, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to execd terminal WebSocket for {ResourceName}.", containerName);
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable,
                    "Failed to connect to sandbox terminal.", CancellationToken.None);
            }

            return;
        }

        var upstreamToClient = PumpWebSocketAsync(clientWs, webSocket, cancellationToken);
        var clientToUpstream = PumpWebSocketAsync(webSocket, clientWs, cancellationToken);

        await Task.WhenAny(upstreamToClient, clientToUpstream);

        if (clientWs.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal closed", CancellationToken.None);
            }
            catch
            {
            }
        }

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal closed", CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    internal async Task<string?> GetSandboxEndpointIpAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _kubernetesClient.CustomObjects.GetNamespacedCustomObjectAsync(
                group: _options.CrdGroup,
                version: _options.CrdVersion,
                namespaceParameter: _options.Namespace,
                plural: _options.CrdPlural,
                name: containerName,
                cancellationToken: cancellationToken);

            var json = JsonSerializer.Serialize(result, JsonOptions);
            var resource = JsonSerializer.Deserialize<BatchSandboxResource>(json, JsonOptions);
            if (resource == null)
            {
                return null;
            }

            // Check annotations for the endpoint IP
            if (resource.Metadata.Annotations != null
                && resource.Metadata.Annotations.TryGetValue(_options.EndpointsAnnotationKey, out var endpointIp)
                && !string.IsNullOrWhiteSpace(endpointIp))
            {
                return endpointIp;
            }

            // Fallback to status.podIP
            return resource.Status?.PodIP;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string MapPhaseToState(string? phase)
    {
        return phase?.ToUpperInvariant() switch
        {
            "PENDING" => SandboxStateNames.Pending,
            "CREATING" => SandboxStateNames.Creating,
            "RUNNING" or "READY" => SandboxStateNames.Running,
            "SUCCEEDED" or "TERMINATED" or "COMPLETED" => SandboxStateNames.Terminated,
            "FAILED" or "ERROR" => SandboxStateNames.Error,
            "DELETING" => SandboxStateNames.Deleting,
            "DELETED" => SandboxStateNames.Deleted,
            _ => SandboxStateNames.Pending
        };
    }

    private static async Task PumpWebSocketAsync(WebSocket source, WebSocket target, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested
               && source.State == WebSocketState.Open
               && target.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            await target.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
        }
    }
}
