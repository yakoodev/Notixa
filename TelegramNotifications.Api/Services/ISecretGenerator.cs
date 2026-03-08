namespace TelegramNotifications.Api.Services;

public interface ISecretGenerator
{
    string GenerateServiceKey();

    string GenerateInviteCode();

    string GeneratePublicId();
}
