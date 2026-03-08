namespace TelegramNotifications.Api.Services;

public sealed record UserContext(long TelegramUserId, bool IsSuperAdmin, bool CanCreateServices);
