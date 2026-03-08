namespace Notixa.Api.Services;

public sealed record NotificationDispatchResult(
    Guid LogId,
    int ResolvedRecipientsCount,
    int SuccessfulDeliveriesCount,
    int FailedDeliveriesCount,
    IReadOnlyCollection<string> Errors);
