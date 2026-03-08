namespace TelegramNotifications.Api.Services;

public interface ISecretHasher
{
    string Hash(string value);
}
