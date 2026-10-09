using System.Security.Cryptography;

namespace LazerRave.Bridge;

internal static class ReplayFile
{
    // ReplayData uses three four-byte slots in the Windows LR2 binary format.
    private const int commandSize = 12;
    public static (string Hash, long Size) Validate(string path, string? expectedHash = null, long? expectedSize = null)
    {
        using var input = File.OpenRead(path);
        long size = input.Length;
        if (size <= 0 || size > 32 * 1024 * 1024 || size % commandSize != 0 || expectedSize is { } savedSize && size != savedSize)
            throw new InvalidDataException("The replay is empty, truncated or has an unsupported size.");
        string hash = Convert.ToHexStringLower(SHA256.HashData(input));
        if (expectedHash is not null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The replay file has changed or is damaged.");
        return (hash, size);
    }
}
