namespace Notixa.Api.Contracts;

public sealed record PreviewInviteResult(
    PreviewInviteStatus Status,
    string? ServicePublicId,
    string? ServiceName,
    string? ExternalUserKey);

public enum PreviewInviteStatus
{
    Invalid = 0,
    Available = 1,
    AlreadySubscribed = 2
}
