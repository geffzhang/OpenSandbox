using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenSandbox.OpenClaw.Contracts;
using OpenSandbox.OpenClaw.Data;
using OpenSandbox.OpenClaw.Domain;
using OpenSandbox.Server.Contracts;

namespace OpenSandbox.OpenClaw.Services;

public sealed class DeploymentService(
    OpenClawDbContext dbContext,
    OpenSandboxGateway gateway,
    SecretProtector secretProtector)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<object> UpsertDeploymentAsync(Guid userId, DeployRequest request, CancellationToken cancellationToken)
    {
        var server = await dbContext.SandboxServers.FirstOrDefaultAsync(x => x.Id == request.SandboxServerId && x.IsEnabled, cancellationToken)
            ?? throw new InvalidOperationException("Sandbox server not found.");
        var template = await dbContext.Templates.FirstOrDefaultAsync(x => x.Id == request.TemplateId && x.IsEnabled, cancellationToken)
            ?? throw new InvalidOperationException("Template not found.");
        var templateVersion = await dbContext.TemplateVersions.FirstOrDefaultAsync(x => x.TemplateId == template.Id && x.Id == template.CurrentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("Template version not found.");
        var settings = await dbContext.SystemSettings.FirstAsync(cancellationToken);
        var user = await dbContext.Users.FirstAsync(x => x.Id == userId, cancellationToken);

        var root = Path.Combine(server.PersistentRootPath, user.UserName);
        Directory.CreateDirectory(root);
        var config = new
        {
            apiEndpoint = request.ApiEndpoint,
            apiType = request.ApiType,
            model = request.Model,
            apiKey = request.ApiKey
        };
        var configPath = Path.Combine(root, templateVersion.ConfigFileName);
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, JsonOptions), cancellationToken);

        var existing = await dbContext.DeploymentInstances.FirstOrDefaultAsync(x => x.UserId == userId && x.SandboxServerId == server.Id, cancellationToken);
        if (existing?.SandboxId is { Length: > 0 })
        {
            await gateway.DeleteSandboxAsync(server.BaseUrl, server.ApiToken, existing.SandboxId, cancellationToken);
        }

        var command = JsonSerializer.Deserialize<List<string>>(templateVersion.CommandJson, JsonOptions) ?? new List<string>();
        var createRequest = new CreateSandboxRequest
        {
            Image = new ImageSpec { Uri = templateVersion.Image },
            Entrypoint = command,
            NeverExpires = true,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["userName"] = user.UserName,
                ["templateId"] = template.Id.ToString(),
                ["templateVersionId"] = templateVersion.Id.ToString(),
                ["sandboxServerId"] = server.Id.ToString()
            },
            ResourceLimits = new ResourceLimits
            {
                Cpu = settings.DefaultCpu,
                Memory = settings.DefaultMemory
            },
            Volumes = new List<VolumeSpec>
            {
                new()
                {
                    Name = "openclaw-config",
                    MountPath = Path.Combine(templateVersion.ConfigMountPath, templateVersion.ConfigFileName).Replace('\\', '/'),
                    Host = new HostVolume { Path = configPath }
                },
                new()
                {
                    Name = "openclaw-data",
                    MountPath = templateVersion.WorkspaceMountPath,
                    Host = new HostVolume { Path = root }
                }
            }
        };

        var created = await gateway.CreateSandboxAsync(server.BaseUrl, server.ApiToken, createRequest, cancellationToken);
        var info = await gateway.GetSandboxAsync(server.BaseUrl, server.ApiToken, created.Id, cancellationToken);

        var instance = existing ?? new DeploymentInstance
        {
            UserId = userId,
            SandboxServerId = server.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        instance.TemplateId = template.Id;
        instance.TemplateVersionId = templateVersion.Id;
        instance.TemplateSnapshotJson = JsonSerializer.Serialize(new
        {
            template.Name,
            templateVersion.Version,
            templateVersion.Image,
            templateVersion.ContainerPort,
            Command = command,
            templateVersion.ConfigMountPath,
            templateVersion.ConfigFileName,
            templateVersion.WorkspaceMountPath
        }, JsonOptions);
        instance.ApiEndpoint = request.ApiEndpoint;
        instance.ApiType = request.ApiType;
        instance.Model = request.Model;
        instance.ApiKeyCipherText = secretProtector.Protect(request.ApiKey);
        instance.SandboxId = created.Id;
        instance.ContainerId = info?.ContainerId;
        instance.PersistentDirectory = root;
        instance.ConfigFilePath = configPath;
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        if (existing == null)
        {
            dbContext.DeploymentInstances.Add(instance);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetDeploymentDetailAsync(instance.Id, cancellationToken) ?? new { };
    }

    public async Task<object?> GetDeploymentDetailByUserAndServerAsync(Guid userId, Guid sandboxServerId, CancellationToken cancellationToken)
    {
        var instance = await dbContext.DeploymentInstances.FirstOrDefaultAsync(x => x.UserId == userId && x.SandboxServerId == sandboxServerId, cancellationToken);
        return instance == null ? null : await GetDeploymentDetailAsync(instance.Id, cancellationToken);
    }

    public async Task<object?> GetDeploymentDetailAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await dbContext.DeploymentInstances
            .Include(x => x.User)
            .Include(x => x.SandboxServer)
            .FirstOrDefaultAsync(x => x.Id == instanceId, cancellationToken);
        if (instance == null)
        {
            return null;
        }

        SandboxInfoResponse? info = null;
        SandboxUsageResponse? stats = null;
        if (!string.IsNullOrWhiteSpace(instance.SandboxId) && instance.SandboxServer != null)
        {
            info = await gateway.GetSandboxAsync(instance.SandboxServer.BaseUrl, instance.SandboxServer.ApiToken, instance.SandboxId, cancellationToken);
            stats = await gateway.GetStatsAsync(instance.SandboxServer.BaseUrl, instance.SandboxServer.ApiToken, instance.SandboxId, cancellationToken);
        }

        return new
        {
            instance.Id,
            instance.SandboxId,
            ContainerId = info?.Status?.Message ?? instance.ContainerId,
            instance.ApiEndpoint,
            instance.ApiType,
            instance.Model,
            instance.PersistentDirectory,
            instance.ConfigFilePath,
            instance.CreatedAt,
            instance.UpdatedAt,
            Server = instance.SandboxServer == null ? null : new { instance.SandboxServer.Id, instance.SandboxServer.Name },
            User = instance.User == null ? null : new { instance.User.Id, instance.User.UserName, instance.User.DisplayName },
            Status = info?.Status?.State,
            CpuPercent = stats?.CpuPercent,
            MemoryPercent = stats?.MemoryPercent,
            MemoryUsage = stats?.MemoryUsage,
            MemoryLimit = stats?.MemoryLimit,
            TemplateSnapshot = string.IsNullOrWhiteSpace(instance.TemplateSnapshotJson) ? null : JsonSerializer.Deserialize<object>(instance.TemplateSnapshotJson, JsonOptions)
        };
    }
}
