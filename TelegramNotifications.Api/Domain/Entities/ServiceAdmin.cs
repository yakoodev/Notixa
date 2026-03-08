namespace TelegramNotifications.Api.Domain.Entities;

public sealed class ServiceAdmin
{
    public Guid ServiceDefinitionId { get; set; }

    public ServiceDefinition ServiceDefinition { get; set; } = null!;

    public long TelegramUserId { get; set; }

    public AppUser User { get; set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
