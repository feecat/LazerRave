namespace Cloud;

public sealed class CloudOptions
{
    public string PublicOrigin { get; init; } = "http://localhost:5080";
    public string StoragePath { get; init; } = "data";
    public bool SecureCookies { get; init; } = true;
    public int MaxRooms { get; init; } = 4;
    public long MaxUploadBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxStorageBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public long MaxTemporaryBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int SessionDays { get; init; } = 7;
}

public sealed class ApiError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
