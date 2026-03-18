using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenSandbox.OpenClaw.Data;
using OpenSandbox.OpenClaw.Domain;
using OpenSandbox.OpenClaw.Options;

namespace OpenSandbox.OpenClaw.Services;

public sealed class AdminBootstrapper(
    OpenClawDbContext dbContext,
    PasswordService passwordService,
    IOptions<AdminBootstrapOptions> bootstrapOptions,
    ILogger<AdminBootstrapper> logger)
{
    private readonly AdminBootstrapOptions _options = bootstrapOptions.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (!await dbContext.SystemSettings.AnyAsync(cancellationToken))
        {
            dbContext.SystemSettings.Add(new SystemSettings());
        }

        if (!await dbContext.Templates.AnyAsync(cancellationToken))
        {
            var template = new DeploymentTemplate
            {
                Name = "官方模板",
                Description = "OpenClaw 官方部署模板",
                IsBuiltin = true,
                IsEnabled = true
            };
            var version = new DeploymentTemplateVersion
            {
                Template = template,
                Version = "v1",
                Image = "ghcr.io/aidotnet/openclaw:latest",
                ContainerPort = 3000,
                CommandJson = "[]",
                ConfigMountPath = "/app/config",
                ConfigFileName = "openclaw.json",
                WorkspaceMountPath = "/app/data",
                IsActive = true
            };
            template.CurrentVersionId = version.Id;
            dbContext.Templates.Add(template);
            dbContext.TemplateVersions.Add(version);
        }

        if (await dbContext.Users.AnyAsync(x => x.Role == UserRole.Admin, cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var userName = Environment.GetEnvironmentVariable(_options.UserNameEnv);
        var password = Environment.GetEnvironmentVariable(_options.PasswordEnv);
        var displayName = Environment.GetEnvironmentVariable(_options.DisplayNameEnv) ?? "Administrator";

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Admin bootstrap skipped because environment variables are missing.");
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var admin = new AppUser
        {
            UserName = userName.Trim(),
            DisplayName = displayName.Trim(),
            Role = UserRole.Admin,
            Status = UserStatus.Active
        };
        admin.PasswordHash = passwordService.HashPassword(admin, password);
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
