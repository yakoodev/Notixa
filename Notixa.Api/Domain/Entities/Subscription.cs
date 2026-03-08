using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Domain.Entities;

public sealed class Subscription
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public ServiceDefinition ServiceDefinition { get; set; } = null!;

    public long TelegramUserId { get; set; }

    public AppUser User { get; set; } = null!;

    public string? ExternalUserKey { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
