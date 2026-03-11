using Microsoft.Extensions.Options;
using OpenSandbox.Abstractions;
using OpenSandbox.Abstractions.Contracts;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Server.Contracts;
using OpenSandbox.Server.Options;

namespace OpenSandbox.Server.Services;

public sealed class OpenSandboxService
{
    private readonly ISandboxRuntime _runtime;
    private readonly ISandboxStore _store;
    private readonly ILogger<OpenSandboxService> _logger;
    private readonly OpenSandboxServerOptions _options;

    public OpenSandboxService(
        ISandboxRuntime runtime,
        ISandboxStore store,
        IOptions<OpenSandboxServerOptions> options,
        ILogger<OpenSandboxService> logger)
    {
        _runtime = runtime;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CreateSandboxResponse> CreateAsync(CreateSandboxRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Image?.Uri))
        {
            throw new InvalidOperationException("image.uri is required.");
        }

        var timeoutSeconds = request.Timeout > 0 ? request.Timeout : 600;
        var record = new SandboxRecord
        {
            Id = Guid.NewGuid().ToString(),
            ContainerName = $"opensandbox-{Guid.NewGuid():N}",
            Image = request.Image.Uri,
            Entrypoint = request.Entrypoint?.Where(static x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>(),
            Metadata = request.Metadata == null ? null : new Dictionary<string, string>(request.Metadata),
            Env = request.Env == null ? null : new Dictionary<string, string>(request.Env),
            Volumes = request.Volumes?.Select(MapVolume).ToList(),
            ResourceLimits = request.ResourceLimits == null
                ? null
                : new SandboxResourceLimits
                {
                    Cpu = request.ResourceLimits.Cpu,
                    Memory = request.ResourceLimits.Memory
                },
            TimeoutSeconds = timeoutSeconds,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds)
        };

        var containerId = await _runtime.CreateAsync(record, cancellationToken);
        record.ContainerId = containerId;
        await RefreshRecordAsync(record, cancellationToken);
        await _store.UpsertAsync(record, cancellationToken);

        return ToCreateResponse(record);
    }

    public async Task<ListSandboxesResponse> ListAsync(int page, int pageSize, IReadOnlyCollection<string> states, IReadOnlyDictionary<string, string> metadataFilters, CancellationToken cancellationToken)
    {
        await DeleteExpiredAsync(cancellationToken);
        var records = (await _store.ListAsync(cancellationToken)).ToList();

        foreach (var record in records)
        {
            await RefreshRecordAsync(record, cancellationToken, save: true);
        }

        IEnumerable<SandboxRecord> query = records;
        if (states.Count > 0)
        {
            query = query.Where(x => states.Contains(x.LastKnownState ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        }

        foreach (var filter in metadataFilters)
        {
            query = query.Where(x => x.Metadata != null
                                     && x.Metadata.TryGetValue(filter.Key, out var value)
                                     && string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase));
        }

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var totalItems = query.Count();
        var items = query
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(ToInfoResponse)
            .ToList();

        return new ListSandboxesResponse
        {
            Items = items,
            Pagination = new PaginationInfo
            {
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalItems = totalItems,
                TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize),
                HasNextPage = normalizedPage * normalizedPageSize < totalItems
            }
        };
    }

    public async Task<SandboxInfoResponse?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        return ToInfoResponse(record);
    }

    public async Task<SandboxUsageResponse?> GetUsageAsync(string id, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        if (!string.Equals(record.LastKnownState, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stats = await _runtime.GetUsageAsync(record.ContainerName, cancellationToken);
        if (stats == null)
        {
            return null;
        }

        return new SandboxUsageResponse
        {
            CpuPercent = stats.CpuPercent,
            MemoryPercent = stats.MemoryPercent,
            CpuLimit = record.ResourceLimits?.Cpu,
            MemoryUsage = stats.MemoryUsage,
            MemoryLimit = record.ResourceLimits?.Memory ?? stats.MemoryLimit,
            CollectedAt = stats.CollectedAt
        };
    }

    public async Task<ExecuteCommandResponse?> ExecuteCommandAsync(string id, string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("command is required.");
        }

        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        if (!string.Equals(record.LastKnownState, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only running sandboxes support command execution.");
        }

        var result = await _runtime.ExecuteAsync(record.ContainerName, command, cancellationToken);
        return result == null
            ? null
            : new ExecuteCommandResponse
            {
                ExitCode = result.ExitCode,
                StdOut = result.StdOut,
                StdErr = result.StdErr
            };
    }

    public async Task<string?> GetTerminalContainerNameAsync(string id, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        return string.Equals(record.LastKnownState, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase)
            ? record.ContainerName
            : null;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var record = await _store.GetAsync(id, cancellationToken);
        if (record == null)
        {
            return false;
        }

        await _runtime.DeleteAsync(record.ContainerName, cancellationToken);
        await _store.RemoveAsync(id, cancellationToken);
        return true;
    }

    public async Task<SandboxInfoResponse?> PauseAsync(string id, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await _runtime.PauseAsync(record.ContainerName, cancellationToken);
        await RefreshRecordAsync(record, cancellationToken, save: true);
        return ToInfoResponse(record);
    }

    public async Task<SandboxInfoResponse?> ResumeAsync(string id, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await _runtime.ResumeAsync(record.ContainerName, cancellationToken);
        await RefreshRecordAsync(record, cancellationToken, save: true);
        return ToInfoResponse(record);
    }

    public async Task<RenewSandboxExpirationResponse?> RenewAsync(string id, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("expiresAt must be later than now.");
        }

        record.ExpiresAt = expiresAt;
        await _store.UpsertAsync(record, cancellationToken);
        return new RenewSandboxExpirationResponse
        {
            ExpiresAt = record.ExpiresAt
        };
    }

    public async Task<EndpointResponse?> GetEndpointAsync(string id, int port, bool useServerProxy, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        if (!string.Equals(record.LastKnownState, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(record.LastKnownState, SandboxStateNames.Paused, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (useServerProxy)
        {
            var path = $"{httpContext.Request.PathBase}/v1/sandboxes/{record.Id}/proxy/{port}/";
            return new EndpointResponse
            {
                EndpointAddress = $"{httpContext.Request.Host}{path}",
                Url = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}",
                Headers = BuildAuthHeaders(httpContext)
            };
        }

        var publishedPort = await _runtime.GetPublishedPortAsync(record.ContainerName, port, cancellationToken);
        if (publishedPort == null)
        {
            return null;
        }

        var host = string.IsNullOrWhiteSpace(_options.EndpointHost)
            ? httpContext.Request.Host.Host
            : _options.EndpointHost;
        var endpointAddress = $"{host}:{publishedPort.Value}";
        return new EndpointResponse
        {
            EndpointAddress = endpointAddress,
            Url = $"{httpContext.Request.Scheme}://{endpointAddress}",
            Headers = BuildAuthHeaders(httpContext)
        };
    }

    public async Task<string?> ResolveProxyDestinationAsync(string id, int port, CancellationToken cancellationToken)
    {
        var record = await GetActiveRecordAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        await RefreshRecordAsync(record, cancellationToken, save: true);
        if (!string.Equals(record.LastKnownState, SandboxStateNames.Running, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var publishedPort = await _runtime.GetPublishedPortAsync(record.ContainerName, port, cancellationToken);
        return publishedPort == null ? null : $"http://{_options.ProxyUpstreamHost}:{publishedPort.Value}";
    }

    public async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var records = await _store.ListAsync(cancellationToken);
        var expired = records.Where(static x => x.ExpiresAt <= DateTimeOffset.UtcNow).ToList();
        foreach (var item in expired)
        {
            try
            {
                await _runtime.DeleteAsync(item.ContainerName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired sandbox {SandboxId}.", item.Id);
            }

            await _store.RemoveAsync(item.Id, cancellationToken);
        }
    }

    private async Task<SandboxRecord?> GetActiveRecordAsync(string id, CancellationToken cancellationToken)
    {
        var record = await _store.GetAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        if (record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await DeleteAsync(id, cancellationToken);
            return null;
        }

        return record;
    }

    private async Task RefreshRecordAsync(SandboxRecord record, CancellationToken cancellationToken, bool save = false)
    {
        var state = await _runtime.InspectAsync(record.ContainerName, cancellationToken);
        if (state == null)
        {
            record.LastKnownState = SandboxStateNames.Deleted;
            record.LastKnownReason = SandboxStateNames.Deleted;
            record.LastKnownMessage = "Container not found";
        }
        else
        {
            record.ContainerId = state.ContainerId;
            record.LastKnownState = state.State;
            record.LastKnownReason = state.Reason;
            record.LastKnownMessage = state.Message;
        }

        if (save)
        {
            await _store.UpsertAsync(record, cancellationToken);
        }
    }

    private static Dictionary<string, string>? BuildAuthHeaders(HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("OpenSandbox.AuthHeaderName", out var headerName)
            || !httpContext.Items.TryGetValue("OpenSandbox.AuthHeaderValue", out var headerValue))
        {
            return null;
        }

        var name = headerName?.ToString();
        var value = headerValue?.ToString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            [name] = value
        };
    }

    private static SandboxVolumeSpec MapVolume(VolumeSpec volume)
    {
        return new SandboxVolumeSpec
        {
            Name = volume.Name,
            MountPath = volume.MountPath,
            ReadOnly = volume.ReadOnly,
            SubPath = volume.SubPath,
            Host = volume.Host == null ? null : new SandboxHostVolume
            {
                Path = volume.Host.Path
            }
        };
    }

    private static CreateSandboxResponse ToCreateResponse(SandboxRecord record)
    {
        return new CreateSandboxResponse
        {
            Id = record.Id,
            Status = ToStatus(record),
            Metadata = record.Metadata == null ? null : new Dictionary<string, string>(record.Metadata),
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt,
            Entrypoint = record.Entrypoint.ToList()
        };
    }

    private static SandboxInfoResponse ToInfoResponse(SandboxRecord record)
    {
        return new SandboxInfoResponse
        {
            Id = record.Id,
            Image = new ImageSpec { Uri = record.Image },
            Entrypoint = record.Entrypoint.ToList(),
            Metadata = record.Metadata == null ? null : new Dictionary<string, string>(record.Metadata),
            Status = ToStatus(record),
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt
        };
    }

    private static SandboxStatus ToStatus(SandboxRecord record)
    {
        return new SandboxStatus
        {
            State = record.LastKnownState ?? SandboxStateNames.Creating,
            Reason = record.LastKnownReason,
            Message = record.LastKnownMessage
        };
    }
}
