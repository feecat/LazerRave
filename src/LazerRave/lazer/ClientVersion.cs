using System.Reflection;

namespace LazerRave.Lazer;

internal static class ClientVersion
{
    public static string Number { get; } = (typeof(ClientVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Application version metadata is missing.")).Split('+')[0];

    public static string DisplayName => $"LazerRave {Number}";

    public static string Stage => Number.Contains("-beta.", StringComparison.Ordinal) ? "BETA"
        : Number.Contains("-rc.", StringComparison.Ordinal) ? "RC" : "STABLE";
}
