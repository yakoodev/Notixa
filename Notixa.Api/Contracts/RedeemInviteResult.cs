namespace Notixa.Api.Contracts;

public sealed record RedeemInviteResult(RedeemInviteStatus Status, SubscriptionListItem? Subscription);

public enum RedeemInviteStatus
{
    Invalid = 0,
    Created = 1,
    AlreadySubscribed = 2
}
