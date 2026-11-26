using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace RepairHub.Application.Auth;

/// <summary>
/// Refresh tokens are 256 bits of CSPRNG output. Only their SHA-256 hash is stored:
/// the token has full entropy, so a fast hash is sufficient and a database leak does not reveal usable tokens.
/// </summary>
public static class RefreshTokenGenerator
{
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
