using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Contracts.Notifications;

public sealed class SendNotificationRequest
{
    public string ServiceKey { get; set; } = string.Empty;

    public string? TemplateKey { get; set; }

    public string? Text { get; set; }

    public TemplateParseMode? ParseMode { get; set; }

    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string>? RecipientExternalKeys { get; set; }

    public bool Broadcast { get; set; }
}
