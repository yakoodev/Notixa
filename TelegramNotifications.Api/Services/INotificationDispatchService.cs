using TelegramNotifications.Api.Contracts.Notifications;

namespace TelegramNotifications.Api.Services;

public interface INotificationDispatchService
{
    Task<NotificationDispatchResult> DispatchAsync(SendNotificationRequest request, CancellationToken cancellationToken);
}
