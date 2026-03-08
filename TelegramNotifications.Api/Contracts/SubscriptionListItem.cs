namespace TelegramNotifications.Api.Contracts;

public sealed record SubscriptionListItem(string ServicePublicId, string ServiceName, string? ExternalUserKey);
