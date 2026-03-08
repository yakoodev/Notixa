namespace TelegramNotifications.Api.Domain.Entities;

public sealed class AppUser
{
    public long TelegramUserId { get; set; }

    public string? Username { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool CanCreateServices { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<ServiceAdmin> AdministeredServices { get; set; } = new List<ServiceAdmin>();

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
