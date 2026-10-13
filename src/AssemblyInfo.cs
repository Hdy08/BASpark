using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BASpark.Tests")]

namespace BASpark;

internal static class BuildInformation
{
    internal static string Version { get; } = "V" + ReadMetadata("BuildVersion");
    internal static string CommitHash { get; } = ReadMetadata("BuildCommitHash");
    internal static long? UnixTimestamp { get; } = long.TryParse(ReadMetadata("BuildUnixTimestamp"),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out long timestamp) ? timestamp : null;

    internal static string? GetCommitUrl(string commitHash) =>
        commitHash.Length == 40 && commitHash.All(Uri.IsHexDigit)
            ? "https://github.com/Hdy08/BASpark/commit/" + commitHash : null;

    internal static string FormatBuildTime(long timestamp, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timestamp), timeZone)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string ReadMetadata(string key) =>
        typeof(BuildInformation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value ?? string.Empty;
}
