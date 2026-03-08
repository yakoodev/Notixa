namespace TelegramNotifications.Api.Contracts.Notifications;

public sealed class SendNotificationResponse
{
    public Guid LogId { get; set; }

    public int ResolvedRecipientsCount { get; set; }

    public int SuccessfulDeliveriesCount { get; set; }

    public int FailedDeliveriesCount { get; set; }

    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
}
