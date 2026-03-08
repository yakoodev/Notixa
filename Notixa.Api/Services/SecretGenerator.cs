namespace Notixa.Api.Services;

public sealed class SecretGenerator : ISecretGenerator
{
    public string GenerateServiceKey() => $"svc_{Guid.NewGuid():N}";

    public string GenerateInviteCode() => $"inv_{Guid.NewGuid():N}";

    public string GeneratePublicId() => Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()[..10];
}
