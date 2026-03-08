using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Domain.Entities;

public sealed class InviteCode
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public ServiceDefinition ServiceDefinition { get; set; } = null!;

    public string CodeHash { get; set; } = string.Empty;

    public InviteCodeType Type { get; set; }

    public string? ExternalUserKey { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int? UsageLimit { get; set; }

    public int UsageCount { get; set; }

    public long CreatedByTelegramUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public bool IsRevoked { get; set; }
}
