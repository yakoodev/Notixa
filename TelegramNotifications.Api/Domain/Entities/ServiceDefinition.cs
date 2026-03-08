using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Domain.Entities;

public sealed class ServiceDefinition
{
    public Guid Id { get; set; }

    public string PublicId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ServiceKeyHash { get; set; } = string.Empty;

    public ServiceStatus Status { get; set; } = ServiceStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<ServiceAdmin> Admins { get; set; } = new List<ServiceAdmin>();

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public ICollection<InviteCode> InviteCodes { get; set; } = new List<InviteCode>();

    public ICollection<NotificationTemplate> Templates { get; set; } = new List<NotificationTemplate>();

    public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
}
