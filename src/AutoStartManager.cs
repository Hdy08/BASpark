using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace BASpark
{
    public readonly record struct AutoStartPlan(bool RegistryRunEnabled, bool ScheduledTaskEnabled);

    public static class AutoStartManager
    {
        public const string RunValueName = "BASpark";
        public const string TaskName = "BASparkAutoStart";

        public static AutoStartPlan CreatePlan(bool autoStart, bool runAsAdmin)
        {
            if (!autoStart)
            {
                return new AutoStartPlan(false, false);
            }

            return runAsAdmin
                ? new AutoStartPlan(false, true)
                : new AutoStartPlan(true, false);
        }

        public static string BuildRunCommand(string exePath)
        {
            return $"\"{exePath}\" --autostart";
        }

        public static string BuildScheduledTaskXml(string exePath, string userId)
        {
            if (!IsExecutablePath(exePath)) throw new ArgumentException("An executable path is required.", nameof(exePath));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user identity is required.", nameof(userId));

            string executable = exePath.Replace("'", "''");
            string directory = (Path.GetDirectoryName(exePath) ?? string.Empty).Replace("'", "''");
            string launcher = $$"""
                $startInfo = New-Object System.Diagnostics.ProcessStartInfo
                $startInfo.FileName = '{{executable}}'
                $startInfo.Arguments = '--autostart'
                $startInfo.WorkingDirectory = '{{directory}}'
                $startInfo.UseShellExecute = $true
                $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
                try {
                    $process = [System.Diagnostics.Process]::Start($startInfo)
                    if ($null -eq $process) { exit 1 }
                    $process.Dispose()
                } catch {
                    Write-Error $_
                    exit 1
                }
                """;
            string arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(launcher));
            XNamespace task = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return new XDocument(
                new XElement(task + "Task", new XAttribute("version", "1.2"),
                    new XElement(task + "Triggers",
                        new XElement(task + "LogonTrigger", new XElement(task + "UserId", userId))),
                    new XElement(task + "Principals",
                        new XElement(task + "Principal", new XAttribute("id", "Author"),
                            new XElement(task + "UserId", userId),
                            new XElement(task + "LogonType", "InteractiveToken"),
                            new XElement(task + "RunLevel", "HighestAvailable"))),
                    new XElement(task + "Settings",
                        new XElement(task + "MultipleInstancesPolicy", "IgnoreNew")),
                    new XElement(task + "Actions", new XAttribute("Context", "Author"),
                        new XElement(task + "Exec",
                            new XElement(task + "Command", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                "WindowsPowerShell", "v1.0", "powershell.exe")),
                            new XElement(task + "Arguments", arguments),
                            new XElement(task + "WorkingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.System)))))).ToString();
        }

        public static string? ResolveExecutablePath(
            string? processPath,
            string? mainModulePath,
            string? assemblyLocation,
            string baseDirectory)
        {
            foreach (string? candidate in new[] { processPath, mainModulePath, assemblyLocation })
            {
                if (IsExecutablePath(candidate))
                {
                    return candidate;
                }
            }

            return string.IsNullOrWhiteSpace(baseDirectory)
                ? null
                : Path.Combine(baseDirectory, "BASpark.exe");
        }

        private static bool IsExecutablePath(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
