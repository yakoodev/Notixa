namespace Notixa.Api.Contracts;

public sealed record CreateInviteResult(string InviteCode, string ServicePublicId, string? ExternalUserKey);
