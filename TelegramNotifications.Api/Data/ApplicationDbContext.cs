using Microsoft.EntityFrameworkCore;
using TelegramNotifications.Api.Domain.Entities;

namespace TelegramNotifications.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<ServiceDefinition> Services => Set<ServiceDefinition>();

    public DbSet<ServiceAdmin> ServiceAdmins => Set<ServiceAdmin>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.TelegramUserId);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Username).HasMaxLength(200);
        });

        modelBuilder.Entity<ServiceDefinition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.PublicId).HasMaxLength(32);
            entity.Property(x => x.ServiceKeyHash).HasMaxLength(128);
            entity.HasIndex(x => x.PublicId).IsUnique();
        });

        modelBuilder.Entity<ServiceAdmin>(entity =>
        {
            entity.HasKey(x => new { x.ServiceDefinitionId, x.TelegramUserId });
            entity.HasOne(x => x.ServiceDefinition).WithMany(x => x.Admins).HasForeignKey(x => x.ServiceDefinitionId);
            entity.HasOne(x => x.User).WithMany(x => x.AdministeredServices).HasForeignKey(x => x.TelegramUserId);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalUserKey).HasMaxLength(256);
            entity.HasOne(x => x.ServiceDefinition)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.ServiceDefinitionId);
            entity.HasOne(x => x.User)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.TelegramUserId);
            entity.HasIndex(x => new { x.ServiceDefinitionId, x.TelegramUserId }).IsUnique();
            entity.HasIndex(x => new { x.ServiceDefinitionId, x.ExternalUserKey });
        });

        modelBuilder.Entity<InviteCode>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(128);
            entity.Property(x => x.ExternalUserKey).HasMaxLength(256);
            entity.HasIndex(x => x.CodeHash).IsUnique();
        });

        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TemplateKey).HasMaxLength(100);
            entity.HasIndex(x => new { x.ServiceDefinitionId, x.TemplateKey }).IsUnique();
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TemplateKey).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.FailureDetails).HasMaxLength(4000);
        });
    }
}
