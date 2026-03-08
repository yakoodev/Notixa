using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Domain.Entities;

public sealed class NotificationTemplate
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public ServiceDefinition ServiceDefinition { get; set; } = null!;

    public string TemplateKey { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public TemplateParseMode ParseMode { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
