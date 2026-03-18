using Microsoft.EntityFrameworkCore;
using OpenSandbox.OpenClaw.Domain;

namespace OpenSandbox.OpenClaw.Data;

public sealed class OpenClawDbContext(DbContextOptions<OpenClawDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SandboxServerNode> SandboxServers => Set<SandboxServerNode>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<DeploymentTemplate> Templates => Set<DeploymentTemplate>();
    public DbSet<DeploymentTemplateVersion> TemplateVersions => Set<DeploymentTemplateVersion>();
    public DbSet<DeploymentInstance> DeploymentInstances => Set<DeploymentInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.Property(x => x.UserName).HasMaxLength(64);
            entity.Property(x => x.DisplayName).HasMaxLength(128);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<SandboxServerNode>(entity =>
        {
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.BaseUrl).HasMaxLength(512);
            entity.Property(x => x.ApiToken).HasMaxLength(512);
            entity.Property(x => x.PersistentRootPath).HasMaxLength(512);
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<DeploymentTemplate>(entity =>
        {
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Description).HasMaxLength(1024);
            entity.HasMany(x => x.Versions)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeploymentTemplateVersion>(entity =>
        {
            entity.HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
            entity.Property(x => x.Version).HasMaxLength(32);
            entity.Property(x => x.Image).HasMaxLength(256);
            entity.Property(x => x.CommandJson).HasColumnType("TEXT");
            entity.Property(x => x.ConfigMountPath).HasMaxLength(256);
            entity.Property(x => x.ConfigFileName).HasMaxLength(128);
            entity.Property(x => x.WorkspaceMountPath).HasMaxLength(256);
        });

        modelBuilder.Entity<DeploymentInstance>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.SandboxServerId }).IsUnique();
            entity.Property(x => x.ApiEndpoint).HasMaxLength(1024);
            entity.Property(x => x.ApiType).HasMaxLength(32);
            entity.Property(x => x.Model).HasMaxLength(128);
            entity.Property(x => x.ApiKeyCipherText).HasColumnType("TEXT");
            entity.Property(x => x.SandboxId).HasMaxLength(128);
            entity.Property(x => x.ContainerId).HasMaxLength(128);
            entity.Property(x => x.PersistentDirectory).HasMaxLength(1024);
            entity.Property(x => x.ConfigFilePath).HasMaxLength(1024);
            entity.Property(x => x.TemplateSnapshotJson).HasColumnType("TEXT");
        });
    }
}
