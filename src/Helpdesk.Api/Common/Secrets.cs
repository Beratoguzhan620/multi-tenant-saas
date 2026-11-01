using System.Security.Cryptography;
using System.Text;

namespace Helpdesk.Api.Common;

/// <summary>Random secrets handed to people (invitation links, customer keys). Only their SHA-256 is stored.</summary>
internal static class Secrets
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[]? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 200)
        {
            return null;
        }

        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        var buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
