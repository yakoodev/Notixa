using System.Security.Cryptography;
using System.Text;

namespace Notixa.Api.Services;

public sealed class Sha256SecretHasher : ISecretHasher
{
    public string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
