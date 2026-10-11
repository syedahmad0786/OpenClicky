using System.Security.Cryptography;
using System.Text;

namespace OpenClicky.Providers;

public static class Pkce
{
    public static string CreateVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    public static string ChallengeS256(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string RandomUrlToken(int bytes = 32) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
