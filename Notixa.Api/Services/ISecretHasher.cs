namespace Notixa.Api.Services;

public interface ISecretHasher
{
    string Hash(string value);
}
