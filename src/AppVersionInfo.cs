using System.Reflection;
using System.Text.RegularExpressions;

namespace BASpark
{
    public static class AppVersionInfo
    {
        private static readonly Regex VersionPattern = new(
            @"^v?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:\.(?<revision>\d+))?(?:-(?<pre>[0-9A-Za-z.-]+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string DisplayVersion { get; } = GetDisplayVersion(Assembly.GetExecutingAssembly());

        public static string UserAgent => $"BASparkClient/{DisplayVersion}";

        public static bool TryCompare(string? candidate, string? baseline, out int result)
        {
            result = 0;
            if (!TryParse(candidate, out AppVersion candidateVersion) ||
                !TryParse(baseline, out AppVersion baselineVersion))
            {
                return false;
            }

            result = candidateVersion.CompareTo(baselineVersion);
            return true;
        }

        private static string GetDisplayVersion(Assembly assembly)
        {
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                return StripBuildMetadata(informational.Trim());
            }

            Version? version = assembly.GetName().Version;
            return version == null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private static string StripBuildMetadata(string version)
        {
            int metadataIndex = version.IndexOf('+');
            return metadataIndex >= 0 ? version[..metadataIndex] : version;
        }

        private static bool TryParse(string? value, out AppVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = VersionPattern.Match(StripBuildMetadata(value.Trim()));
            if (!match.Success)
            {
                return false;
            }

            if (!TryReadPart(match, "major", 0, out int major) ||
                !TryReadPart(match, "minor", 0, out int minor) ||
                !TryReadPart(match, "patch", 0, out int patch) ||
                !TryReadPart(match, "revision", 0, out int revision))
            {
                return false;
            }

            version = new AppVersion(major, minor, patch, revision, match.Groups["pre"].Value);
            return true;
        }

        private static bool TryReadPart(Match match, string name, int fallback, out int value)
        {
            Group group = match.Groups[name];
            if (!group.Success || string.IsNullOrEmpty(group.Value))
            {
                value = fallback;
                return true;
            }

            return int.TryParse(group.Value, out value);
        }

        private readonly struct AppVersion : IComparable<AppVersion>
        {
            private readonly int _major;
            private readonly int _minor;
            private readonly int _patch;
            private readonly int _revision;
            private readonly string _preRelease;

            public AppVersion(int major, int minor, int patch, int revision, string preRelease)
            {
                _major = major;
                _minor = minor;
                _patch = patch;
                _revision = revision;
                _preRelease = preRelease ?? string.Empty;
            }

            public int CompareTo(AppVersion other)
            {
                int numeric = ComparePart(_major, other._major);
                if (numeric != 0) return numeric;
                numeric = ComparePart(_minor, other._minor);
                if (numeric != 0) return numeric;
                numeric = ComparePart(_patch, other._patch);
                if (numeric != 0) return numeric;
                numeric = ComparePart(_revision, other._revision);
                if (numeric != 0) return numeric;

                bool hasPreRelease = !string.IsNullOrEmpty(_preRelease);
                bool otherHasPreRelease = !string.IsNullOrEmpty(other._preRelease);
                if (hasPreRelease != otherHasPreRelease)
                {
                    return hasPreRelease ? -1 : 1;
                }

                return ComparePreRelease(_preRelease, other._preRelease);
            }

            private static int ComparePart(int left, int right) => left.CompareTo(right);

            private static int ComparePreRelease(string left, string right)
            {
                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                string[] leftParts = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
                string[] rightParts = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
                int count = Math.Min(leftParts.Length, rightParts.Length);
                for (int i = 0; i < count; i++)
                {
                    bool leftNumeric = int.TryParse(leftParts[i], out int leftNumber);
                    bool rightNumeric = int.TryParse(rightParts[i], out int rightNumber);
                    if (leftNumeric && rightNumeric)
                    {
                        int numeric = leftNumber.CompareTo(rightNumber);
                        if (numeric != 0) return numeric;
                        continue;
                    }

                    if (leftNumeric != rightNumeric)
                    {
                        return leftNumeric ? -1 : 1;
                    }

                    int ordinal = string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                    if (ordinal != 0) return ordinal;
                }

                return leftParts.Length.CompareTo(rightParts.Length);
            }
        }
    }
}
