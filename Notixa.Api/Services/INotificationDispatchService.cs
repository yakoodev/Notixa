using Notixa.Api.Contracts.Notifications;

namespace Notixa.Api.Services;

public interface INotificationDispatchService
{
    Task<NotificationDispatchResult> DispatchAsync(SendNotificationRequest request, CancellationToken cancellationToken);
}
