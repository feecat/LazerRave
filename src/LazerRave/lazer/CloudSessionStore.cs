using System.Security.Cryptography;
using System.Text;

namespace LazerRave.Lazer;

internal sealed record CloudSession(string Server, string Token);

internal sealed class CloudSessionStore(string path)
{
    private static readonly byte[] entropy = Encoding.UTF8.GetBytes("LazerRave cloud session v1");
    public bool Exists => File.Exists(path);

    public void Save(Uri server, string token)
    {
        var bytes = Encoding.UTF8.GetBytes(server.AbsoluteUri + "\n" + token);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            try { File.WriteAllBytes(temporary, protectedBytes); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public CloudSession? Read()
    {
        if (!Exists) return null;
        try
        {
            if (new FileInfo(path).Length > 16384) throw new CryptographicException("Invalid session file.");
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            try
            {
                var parts = Encoding.UTF8.GetString(bytes).Split('\n');
                if (parts.Length != 2 || parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit))
                    throw new CryptographicException("Invalid session data.");
                return new(CloudClient.ServerUri(parts[0]).AbsoluteUri, parts[1]);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException)
        {
            Clear();
            return null;
        }
    }

    public void Clear()
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { }
    }
}
