using System.Net;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenSandbox.Abstractions;
using OpenSandbox.Abstractions.Contracts;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Runtime.Kubernetes.Options;

namespace OpenSandbox.Runtime.Kubernetes.Tests;

public sealed class KubernetesSandboxRuntimeTests
{
    private readonly IKubernetes _mockClient;
    private readonly ICustomObjectsOperations _mockCustomObjects;
    private readonly KubernetesRuntimeOptions _options;
    private readonly KubernetesSandboxRuntime _runtime;

    public KubernetesSandboxRuntimeTests()
    {
        _mockClient = Substitute.For<IKubernetes>();
        _mockCustomObjects = Substitute.For<ICustomObjectsOperations>();
        _mockClient.CustomObjects.Returns(_mockCustomObjects);

        _options = new KubernetesRuntimeOptions
        {
            Namespace = "test-ns",
            CrdGroup = "sandbox.opensandbox.io",
            CrdVersion = "v1alpha1",
            CrdPlural = "batchsandboxes",
            PoolName = "default-pool",
            ExecdPort = 44772,
            EndpointsAnnotationKey = "sandbox.opensandbox.io/endpoints"
        };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        _runtime = new KubernetesSandboxRuntime(
            _mockClient,
            Microsoft.Extensions.Options.Options.Create(_options),
            httpClientFactory,
            NullLogger<KubernetesSandboxRuntime>.Instance);
    }

    [Fact]
    public async Task CreateAsync_SubmitsCrdToKubernetes()
    {
        var record = new SandboxRecord
        {
            Id = "test-id-123",
            ContainerName = "sandbox-test-id-123",
            Image = "opensandbox/test:latest",
            Entrypoint = new List<string> { "bash" },
            Metadata = new Dictionary<string, string> { ["tenant"] = "demo" },
            Env = new Dictionary<string, string> { ["MY_VAR"] = "value" },
            ResourceLimits = new SandboxResourceLimits { Cpu = "500m", Memory = "512Mi" },
            TimeoutSeconds = 600
        };

        SetupCreateReturnsSuccess();

        var containerName = await _runtime.CreateAsync(record, CancellationToken.None);

        Assert.Equal("sandbox-test-id-123", containerName);

        await _mockCustomObjects.Received(1).CreateNamespacedCustomObjectWithHttpMessagesAsync(
            Arg.Any<object>(),
            Arg.Is("sandbox.opensandbox.io"),
            Arg.Is("v1alpha1"),
            Arg.Is("test-ns"),
            Arg.Is("batchsandboxes"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithNoEntrypoint_SubmitsCrd()
    {
        var record = new SandboxRecord
        {
            Id = "test-id-no-ep",
            ContainerName = "sandbox-test-id-no-ep",
            Image = "opensandbox/test:latest",
            Entrypoint = new List<string>(),
            TimeoutSeconds = 300
        };

        SetupCreateReturnsSuccess();

        var containerName = await _runtime.CreateAsync(record, CancellationToken.None);

        Assert.Equal("sandbox-test-id-no-ep", containerName);
    }

    [Fact]
    public async Task DeleteAsync_DeletesCrdFromKubernetes()
    {
        SetupDeleteReturnsSuccess();

        await _runtime.DeleteAsync("sandbox-test-id", CancellationToken.None);

        await _mockCustomObjects.Received(1).DeleteNamespacedCustomObjectWithHttpMessagesAsync(
            Arg.Is("sandbox.opensandbox.io"),
            Arg.Is("v1alpha1"),
            Arg.Is("test-ns"),
            Arg.Is("batchsandboxes"),
            Arg.Is("sandbox-test-id"),
            Arg.Any<V1DeleteOptions?>(),
            Arg.Any<int?>(),
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_HandlesNotFound()
    {
        var notFoundResponse = new HttpResponseMessageWrapper(
            new HttpResponseMessage(HttpStatusCode.NotFound), string.Empty);

        _mockCustomObjects
            .DeleteNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpOperationException { Response = notFoundResponse });

        // Should not throw
        await _runtime.DeleteAsync("sandbox-nonexistent", CancellationToken.None);
    }

    [Fact]
    public async Task InspectAsync_ReturnsRunningState()
    {
        SetupGetReturnsResource("RUNNING", podName: "sandbox-test-pod-abc", podIP: "10.0.0.5", message: "Sandbox is running");

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(SandboxStateNames.Running, state.State);
        Assert.Equal("sandbox-test-pod-abc", state.ContainerId);
    }

    [Fact]
    public async Task InspectAsync_ReturnsPendingState()
    {
        SetupGetReturnsResource("PENDING", message: "Waiting for resources");

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(SandboxStateNames.Pending, state.State);
    }

    [Fact]
    public async Task InspectAsync_ReturnsTerminatedState()
    {
        SetupGetReturnsResource("TERMINATED");

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(SandboxStateNames.Terminated, state.State);
    }

    [Fact]
    public async Task InspectAsync_ReturnsErrorForFailedPhase()
    {
        SetupGetReturnsResource("FAILED", message: "OOM killed");

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(SandboxStateNames.Error, state.State);
    }

    [Fact]
    public async Task InspectAsync_ReturnsReadyAsRunning()
    {
        SetupGetReturnsResource("READY", podIP: "10.0.0.5");

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(SandboxStateNames.Running, state.State);
    }

    [Fact]
    public async Task InspectAsync_ReturnsNullWhenNotFound()
    {
        var notFoundResponse = new HttpResponseMessageWrapper(
            new HttpResponseMessage(HttpStatusCode.NotFound), string.Empty);

        _mockCustomObjects
            .GetNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpOperationException { Response = notFoundResponse });

        var state = await _runtime.InspectAsync("sandbox-nonexistent", CancellationToken.None);

        Assert.Null(state);
    }

    [Fact]
    public async Task PauseAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _runtime.PauseAsync("sandbox-test", CancellationToken.None));
    }

    [Fact]
    public async Task ResumeAsync_ThrowsNotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _runtime.ResumeAsync("sandbox-test", CancellationToken.None));
    }

    [Fact]
    public async Task GetPublishedPortAsync_ReturnsContainerPortWhenEndpointAvailable()
    {
        SetupGetReturnsResourceWithAnnotation("RUNNING", "10.0.0.5");

        var port = await _runtime.GetPublishedPortAsync("sandbox-test", 8080, CancellationToken.None);

        Assert.Equal(8080, port);
    }

    [Fact]
    public async Task GetPublishedPortAsync_ReturnsNullWhenNoEndpoint()
    {
        var notFoundResponse = new HttpResponseMessageWrapper(
            new HttpResponseMessage(HttpStatusCode.NotFound), string.Empty);

        _mockCustomObjects
            .GetNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpOperationException { Response = notFoundResponse });

        var port = await _runtime.GetPublishedPortAsync("sandbox-test", 8080, CancellationToken.None);

        Assert.Null(port);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsNullForNonRunning()
    {
        SetupGetReturnsResource("PENDING");

        var usage = await _runtime.GetUsageAsync("sandbox-test", CancellationToken.None);

        Assert.Null(usage);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUsageForRunning()
    {
        SetupGetReturnsResource("RUNNING", podName: "sandbox-test-pod");

        var usage = await _runtime.GetUsageAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(usage);
        Assert.True(usage.CollectedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AddKubernetesRuntime_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<KubernetesRuntimeOptions>(opts =>
        {
            opts.Namespace = "test";
        });

        // Register a mock IKubernetes so we don't try to connect to a real cluster
        var mockK8s = Substitute.For<IKubernetes>();
        services.AddSingleton(mockK8s);

        services.AddKubernetesRuntime();

        var provider = services.BuildServiceProvider();
        var runtime = provider.GetService<ISandboxRuntime>();

        Assert.NotNull(runtime);
        Assert.IsType<KubernetesSandboxRuntime>(runtime);
    }

    [Theory]
    [InlineData("PENDING", SandboxStateNames.Pending)]
    [InlineData("CREATING", SandboxStateNames.Creating)]
    [InlineData("RUNNING", SandboxStateNames.Running)]
    [InlineData("READY", SandboxStateNames.Running)]
    [InlineData("SUCCEEDED", SandboxStateNames.Terminated)]
    [InlineData("TERMINATED", SandboxStateNames.Terminated)]
    [InlineData("COMPLETED", SandboxStateNames.Terminated)]
    [InlineData("FAILED", SandboxStateNames.Error)]
    [InlineData("ERROR", SandboxStateNames.Error)]
    [InlineData("DELETING", SandboxStateNames.Deleting)]
    [InlineData("DELETED", SandboxStateNames.Deleted)]
    [InlineData(null, SandboxStateNames.Pending)]
    [InlineData("UNKNOWN", SandboxStateNames.Pending)]
    public async Task InspectAsync_MapsPhaseCorrectly(string? phase, string expectedState)
    {
        SetupGetReturnsResource(phase);

        var state = await _runtime.InspectAsync("sandbox-test", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(expectedState, state.State);
    }

    private void SetupCreateReturnsSuccess()
    {
        var httpResponse = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{}")
        };

        _mockCustomObjects
            .CreateNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<object>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<object>
            {
                Body = new object(),
                Response = httpResponse
            }));
    }

    private void SetupDeleteReturnsSuccess()
    {
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        };

        _mockCustomObjects
            .DeleteNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<object>
            {
                Body = new object(),
                Response = httpResponse
            }));
    }

    private void SetupGetReturnsResource(string? phase, string? podName = null, string? podIP = null, string? message = null)
    {
        var crdResponse = new
        {
            apiVersion = "sandbox.opensandbox.io/v1alpha1",
            kind = "BatchSandbox",
            metadata = new
            {
                name = "sandbox-test",
                @namespace = "test-ns"
            },
            status = new
            {
                phase,
                podName,
                podIP,
                message
            }
        };

        var json = JsonSerializer.Serialize(crdResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        _mockCustomObjects
            .GetNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<object>
            {
                Body = crdResponse,
                Response = httpResponse
            }));
    }

    private void SetupGetReturnsResourceWithAnnotation(string phase, string endpointIp)
    {
        var crdResponse = new
        {
            apiVersion = "sandbox.opensandbox.io/v1alpha1",
            kind = "BatchSandbox",
            metadata = new
            {
                name = "sandbox-test",
                @namespace = "test-ns",
                annotations = new Dictionary<string, string>
                {
                    ["sandbox.opensandbox.io/endpoints"] = endpointIp
                }
            },
            status = new
            {
                phase,
                podIP = endpointIp
            }
        };

        var json = JsonSerializer.Serialize(crdResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        _mockCustomObjects
            .GetNamespacedCustomObjectWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<object>
            {
                Body = crdResponse,
                Response = httpResponse
            }));
    }
}
