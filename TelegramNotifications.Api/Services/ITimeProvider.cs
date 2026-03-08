namespace TelegramNotifications.Api.Services;

public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}
