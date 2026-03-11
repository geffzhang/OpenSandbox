using System.Net.WebSockets;
using OpenSandbox.Abstractions.Contracts;

namespace OpenSandbox.Abstractions.Services;

public interface ISandboxRuntime
{
    Task<string> CreateAsync(SandboxRecord record, CancellationToken cancellationToken);
    Task DeleteAsync(string containerName, CancellationToken cancellationToken);
    Task PauseAsync(string containerName, CancellationToken cancellationToken);
    Task ResumeAsync(string containerName, CancellationToken cancellationToken);
    Task<SandboxRuntimeState?> InspectAsync(string containerName, CancellationToken cancellationToken);
    Task<int?> GetPublishedPortAsync(string containerName, int containerPort, CancellationToken cancellationToken);
    Task<SandboxRuntimeUsage?> GetUsageAsync(string containerName, CancellationToken cancellationToken);
    Task<SandboxCommandResult?> ExecuteAsync(string containerName, string command, CancellationToken cancellationToken);
    Task RunTerminalSessionAsync(string containerName, WebSocket webSocket, CancellationToken cancellationToken);
}
