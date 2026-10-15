using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace BASpark
{
    public readonly record struct AutoStartPlan(bool RegistryRunEnabled, bool ScheduledTaskEnabled);

    internal interface IAutoStartRegistration
    {
        string? RunCommand { get; set; }
        string? TaskXml { get; }
        void RegisterTask(string xml);
        void DeleteTask();
    }

    public static class AutoStartManager
    {
        public const string RunValueName = "BASpark";
        public const string TaskName = "BASparkAutoStart";
        private static readonly object SyncLock = new();

        internal static void ApplySettings(bool autoStart, bool runAsAdmin, bool allowElevation = true, string? executablePath = null)
        {
            string? exePath = executablePath ?? ResolveExecutablePath(Environment.ProcessPath, null, null, AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(exePath)) throw new InvalidOperationException("Cannot resolve the auto-start executable.");
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string userId = identity.User?.Value ?? throw new InvalidOperationException("Cannot resolve the auto-start task user.");
            lock (SyncLock)
                Synchronize(CreatePlan(autoStart, runAsAdmin), exePath, userId, new WindowsAutoStartRegistration(allowElevation));
        }

        internal static void Synchronize(AutoStartPlan plan, string exePath, string userId, IAutoStartRegistration registration)
        {
            if (plan.ScheduledTaskEnabled)
            {
                if (!IsScheduledTaskCurrent(registration.TaskXml, exePath, userId))
                {
                    registration.RegisterTask(BuildScheduledTaskXml(exePath, userId));
                    if (!IsScheduledTaskCurrent(registration.TaskXml, exePath, userId))
                        throw new InvalidOperationException("The auto-start task could not be verified after registration.");
                }
                if (registration.RunCommand != null) registration.RunCommand = null;
            }
            else
            {
                string? command = plan.RegistryRunEnabled ? BuildRunCommand(exePath) : null;
                if (!string.Equals(registration.RunCommand, command, StringComparison.Ordinal)) registration.RunCommand = command;
                if (registration.TaskXml != null) registration.DeleteTask();
            }
        }

        internal static bool IsScheduledTaskCurrent(string? xml, string exePath, string userId)
        {
            if (string.IsNullOrWhiteSpace(xml)) return false;
            try
            {
                XElement actual = XDocument.Parse(xml).Root!;
                XElement expected = XDocument.Parse(BuildScheduledTaskXml(exePath, userId)).Root!;
                XNamespace task = expected.Name.Namespace;
                XElement? principal = actual.Element(task + "Principals")?.Element(task + "Principal");
                XElement expectedPrincipal = expected.Element(task + "Principals")!.Element(task + "Principal")!;
                XElement? triggers = actual.Element(task + "Triggers");
                XElement? settings = actual.Element(task + "Settings");
                return actual.Name == task + "Task" &&
                    principal != null && MatchesTaskUser((string?)principal.Element(task + "UserId"), userId) &&
                    expectedPrincipal.Elements().Where(element => element.Name != task + "UserId")
                        .All(element => (string?)principal.Element(element.Name) == element.Value) &&
                    triggers?.Elements().Count() == 1 &&
                    MatchesTaskUser((string?)triggers.Element(task + "LogonTrigger")?.Element(task + "UserId"), userId) &&
                    !string.Equals((string?)triggers.Element(task + "LogonTrigger")?.Element(task + "Enabled"), "false", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals((string?)settings?.Element(task + "Enabled"), "false", StringComparison.OrdinalIgnoreCase) &&
                    XNode.DeepEquals(actual.Element(task + "Actions"), expected.Element(task + "Actions"));
            }
            catch (System.Xml.XmlException) { return false; }
        }

        private static bool MatchesTaskUser(string? actual, string userId)
        {
            if (string.Equals(actual, userId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrWhiteSpace(actual)) return false;
            try
            {
                return string.Equals(actual, new SecurityIdentifier(userId).Translate(typeof(NTAccount)).Value, StringComparison.OrdinalIgnoreCase);
            }
            catch (IdentityNotMappedException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private sealed class WindowsAutoStartRegistration(bool allowElevation) : IAutoStartRegistration
        {
            private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

            public string? RunCommand
            {
                get
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(RunValueName) as string;
                }
                set
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
                        ?? throw new InvalidOperationException("Cannot open the auto-start registry key.");
                    if (value == null) key.DeleteValue(RunValueName, false);
                    else key.SetValue(RunValueName, value);
                }
            }

            public string? TaskXml
            {
                get
                {
                    object? service = null;
                    object? folder = null;
                    object? registered = null;
                    try
                    {
                        service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!);
                        ((dynamic)service!).Connect();
                        folder = ((dynamic)service).GetFolder(@"\");
                        registered = ((dynamic)folder).GetTask(TaskName);
                        return (string)((dynamic)registered).Xml;
                    }
                    catch (Exception exception) when (exception.HResult == unchecked((int)0x80070002)) { return null; }
                    finally
                    {
                        foreach (object? instance in new[] { registered, folder, service })
                            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
                    }
                }
            }

            public void RegisterTask(string xml)
            {
                string file = Path.Combine(Path.GetTempPath(), $"BASpark-autostart-{Guid.NewGuid():N}.xml");
                try
                {
                    File.WriteAllText(file, xml);
                    RunScheduler($"/create /tn \"{TaskName}\" /xml \"{file}\" /f");
                }
                finally { File.Delete(file); }
            }

            public void DeleteTask() => RunScheduler($"/delete /tn \"{TaskName}\" /f");

            private void RunScheduler(string arguments)
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                if (!elevated && !allowElevation)
                    throw new UnauthorizedAccessException("Administrator privileges are required to repair the auto-start task.");
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                    Arguments = arguments,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = elevated ? string.Empty : "runas"
                };
                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Cannot start the task scheduler configuration process.");
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Task scheduler configuration failed (exit code {process.ExitCode}).");
            }
        }

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
