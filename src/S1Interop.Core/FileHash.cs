using System.Security.Cryptography;

namespace S1Interop.Core;

internal static class FileHash
{
    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
