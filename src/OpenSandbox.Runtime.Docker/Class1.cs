using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSandbox.Abstractions;
using OpenSandbox.Abstractions.Contracts;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Runtime.Docker.Options;

namespace OpenSandbox.Runtime.Docker;

public sealed class DockerSandboxRuntime : ISandboxRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DockerRuntimeOptions _options;
    private readonly ILogger<DockerSandboxRuntime> _logger;

    public DockerSandboxRuntime(IOptions<DockerRuntimeOptions> options, ILogger<DockerSandboxRuntime> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateAsync(SandboxRecord record, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name", record.ContainerName,
            "--label", "opensandbox.managed=true",
            "--label", $"opensandbox.id={record.Id}"
        };

        foreach (var port in _options.PublishedPorts.Distinct())
        {
            arguments.Add("-p");
            arguments.Add($"0:{port}");
        }

        var cpuLimit = ConvertCpu(record.ResourceLimits?.Cpu);
        if (!string.IsNullOrWhiteSpace(cpuLimit))
        {
            arguments.Add("--cpus");
            arguments.Add(cpuLimit);
        }

        var memoryLimit = ConvertMemory(record.ResourceLimits?.Memory);
        if (!string.IsNullOrWhiteSpace(memoryLimit))
        {
            arguments.Add("--memory");
            arguments.Add(memoryLimit);
        }

        if (record.Env != null)
        {
            foreach (var item in record.Env)
            {
                arguments.Add("-e");
                arguments.Add($"{item.Key}={item.Value}");
            }
        }

        if (record.Metadata != null)
        {
            foreach (var item in record.Metadata)
            {
                arguments.Add("--label");
                arguments.Add($"opensandbox.meta.{item.Key}={item.Value}");
            }
        }

        if (record.Volumes != null)
        {
            foreach (var volume in record.Volumes)
            {
                if (string.IsNullOrWhiteSpace(volume.MountPath) || string.IsNullOrWhiteSpace(volume.Host?.Path))
                {
                    continue;
                }

                var hostPath = volume.Host.Path;
                if (!string.IsNullOrWhiteSpace(volume.SubPath))
                {
                    hostPath = Path.Combine(hostPath, volume.SubPath);
                }

                var mount = $"{hostPath}:{volume.MountPath}";
                if (volume.ReadOnly == true)
                {
                    mount += ":ro";
                }

                arguments.Add("-v");
                arguments.Add(mount);
            }
        }

        if (record.Entrypoint.Count > 0)
        {
            arguments.Add("--entrypoint");
            arguments.Add(record.Entrypoint[0]);
        }

        arguments.Add(record.Image);

        if (record.Entrypoint.Count > 1)
        {
            arguments.AddRange(record.Entrypoint.Skip(1));
        }

        var result = await ExecuteProcessAsync(arguments, cancellationToken, throwOnError: true);
        return result.StdOut.Trim();
    }

    public async Task DeleteAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["rm", "-f", containerName], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0 && !ContainsNoSuchContainer(result.StdErr))
        {
            throw new InvalidOperationException($"Failed to delete Docker container: {result.StdErr}");
        }
    }

    public async Task PauseAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["pause", containerName], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0 && !ContainsAlreadyPaused(result.StdErr))
        {
            throw new InvalidOperationException($"Failed to pause Docker container: {result.StdErr}");
        }
    }

    public async Task ResumeAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["unpause", containerName], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0 && !ContainsNotPaused(result.StdErr))
        {
            throw new InvalidOperationException($"Failed to resume Docker container: {result.StdErr}");
        }
    }

    public async Task<SandboxRuntimeState?> InspectAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["inspect", containerName], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0)
        {
            if (ContainsNoSuchContainer(result.StdErr))
            {
                return null;
            }

            throw new InvalidOperationException($"Failed to inspect Docker container: {result.StdErr}");
        }

        var items = JsonSerializer.Deserialize<List<DockerInspectResponse>>(result.StdOut, JsonOptions);
        var item = items?.FirstOrDefault();
        if (item == null)
        {
            return null;
        }

        var state = MapState(item.State);
        state.ContainerId = item.Id;
        return state;
    }

    public async Task<int?> GetPublishedPortAsync(string containerName, int containerPort, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["port", containerName, $"{containerPort}/tcp"], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0)
        {
            return ContainsNoSuchContainer(result.StdErr) ? null : null;
        }

        var lines = result.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var separator = line.LastIndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var portText = line[(separator + 1)..].Trim();
            if (int.TryParse(portText, out var port))
            {
                return port;
            }
        }

        return null;
    }

    public async Task<SandboxRuntimeUsage?> GetUsageAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["stats", "--no-stream", "--format", "{{json .}}", containerName], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0)
        {
            if (ContainsNoSuchContainer(result.StdErr))
            {
                return null;
            }

            throw new InvalidOperationException($"Failed to read Docker stats: {result.StdErr}");
        }

        var json = result.StdOut.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var item = JsonSerializer.Deserialize<DockerStatsResponse>(json, JsonOptions);
        if (item == null)
        {
            return null;
        }

        var (memoryUsage, memoryLimit) = ParseMemoryUsage(item.MemoryUsage);
        return new SandboxRuntimeUsage
        {
            CpuPercent = ParsePercent(item.CpuPercent),
            MemoryPercent = ParsePercent(item.MemoryPercent),
            MemoryUsage = memoryUsage,
            MemoryLimit = memoryLimit,
            CollectedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<SandboxCommandResult?> ExecuteAsync(string containerName, string command, CancellationToken cancellationToken)
    {
        var result = await ExecuteProcessAsync(["exec", containerName, "sh", "-lc", command], cancellationToken, throwOnError: false);
        if (result.ExitCode != 0 && ContainsNoSuchContainer(result.StdErr))
        {
            return null;
        }

        return new SandboxCommandResult
        {
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };
    }

    public async Task RunTerminalSessionAsync(string containerName, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DockerCommand,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(containerName);
        startInfo.ArgumentList.Add("sh");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add("export TERM=xterm-256color; if command -v bash >/dev/null 2>&1; then exec bash -i; else exec sh -i; fi");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var sendLock = new SemaphoreSlim(1, 1);
        var stdoutTask = PumpStreamToWebSocketAsync(process.StandardOutput.BaseStream, webSocket, sendLock, cancellationToken);
        var stderrTask = PumpStreamToWebSocketAsync(process.StandardError.BaseStream, webSocket, sendLock, cancellationToken);
        var stdinTask = PumpWebSocketToStreamAsync(webSocket, process.StandardInput, cancellationToken);

        await Task.WhenAny(stdoutTask, stderrTask, stdinTask, process.WaitForExitAsync(cancellationToken));

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
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

    private async Task<ProcessResult> ExecuteProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool throwOnError)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DockerCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        if (throwOnError && process.ExitCode != 0)
        {
            _logger.LogError("Docker command failed: {Arguments}; stderr={StdErr}", string.Join(' ', arguments), stdErr);
            throw new InvalidOperationException($"Docker command failed: {stdErr}");
        }

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static SandboxRuntimeState MapState(DockerStateResponse? state)
    {
        var status = state?.Status?.ToLowerInvariant();
        var mappedState = status switch
        {
            "created" => SandboxStateNames.Creating,
            "running" => state?.Paused == true ? SandboxStateNames.Paused : SandboxStateNames.Running,
            "paused" => SandboxStateNames.Paused,
            "restarting" => SandboxStateNames.Resuming,
            "removing" => SandboxStateNames.Deleting,
            "dead" => SandboxStateNames.Error,
            "exited" => SandboxStateNames.Error,
            _ => SandboxStateNames.Error
        };

        var message = state?.Error;
        if (string.IsNullOrWhiteSpace(message) && string.Equals(status, "exited", StringComparison.OrdinalIgnoreCase))
        {
            message = $"Container exited with code {state?.ExitCode}";
        }

        return new SandboxRuntimeState
        {
            State = mappedState,
            Reason = status,
            Message = message
        };
    }

    private static string? ConvertCpu(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.EndsWith("m", StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(value[..^1], CultureInfo.InvariantCulture, out var milli))
        {
            return (milli / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(value, CultureInfo.InvariantCulture, out var cpu))
        {
            return cpu.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string? ConvertMemory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.EndsWith("Gi", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^2] + "g";
        }

        if (value.EndsWith("Mi", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^2] + "m";
        }

        if (value.EndsWith("G", StringComparison.OrdinalIgnoreCase) || value.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        return value;
    }

    private static decimal? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim().TrimEnd('%');
        return decimal.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static (string? Usage, string? Limit) ParseMemoryUsage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var parts = value.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => (parts[0], parts[1]),
            1 => (parts[0], null),
            _ => (null, null)
        };
    }

    private static async Task PumpStreamToWebSocketAsync(Stream stream, WebSocket webSocket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested && (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived))
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                await webSocket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                sendLock.Release();
            }
        }
    }

    private static async Task PumpWebSocketToStreamAsync(WebSocket webSocket, StreamWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await writer.WriteAsync(text);
            await writer.FlushAsync(cancellationToken);
        }
    }

    private static bool ContainsNoSuchContainer(string message)
    {
        return message.Contains("No such container", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No such object", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAlreadyPaused(string message)
    {
        return message.Contains("is already paused", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNotPaused(string message)
    {
        return message.Contains("is not paused", StringComparison.OrdinalIgnoreCase);
    }
}
