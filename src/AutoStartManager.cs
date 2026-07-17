using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
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

        public static string BuildScheduledTaskRunCommand(string exePath, string systemDirectory)
        {
            string launcherPath = Path.Combine(systemDirectory, "cmd.exe");
            return $"\"{launcherPath}\" {BuildScheduledTaskLauncherArguments(exePath)}";
        }

        public static string BuildScheduledTaskLauncherArguments(string exePath)
        {
            if (!Path.IsPathFullyQualified(exePath) ||
                !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                exePath.IndexOfAny(new[] { '"', '%', '\r', '\n' }) >= 0)
            {
                throw new ArgumentException("The scheduled-task executable path is not safe.", nameof(exePath));
            }

            return $"/d /v:off /s /c start \"\" /b \"{exePath}\" --autostart";
        }

        public static IReadOnlyList<string> BuildScheduledTaskArguments(
            string taskName,
            string exePath,
            string userName,
            string systemDirectory,
            bool create)
        {
            if (!create)
            {
                return new[] { "/delete", "/tn", taskName, "/f" };
            }

            return new[]
            {
                "/create",
                "/tn", taskName,
                "/tr", BuildScheduledTaskRunCommand(exePath, systemDirectory),
                "/sc", "onlogon",
                "/ru", userName,
                "/rl", "highest",
                "/it",
                "/f"
            };
        }

        public static bool IsScheduledTaskOperationSuccessful(bool create, int exitCode, bool taskExists)
        {
            return create
                ? exitCode == 0 && taskExists
                : exitCode == 0 || !taskExists;
        }

        public static bool IsScheduledTaskDefinitionCurrent(
            string xml,
            string exePath,
            string systemDirectory,
            string userSid)
        {
            try
            {
                XDocument document = XDocument.Parse(xml);
                XElement? task = document.Root;
                XElement? triggers = GetChild(task, "Triggers");
                XElement? principals = GetChild(task, "Principals");
                XElement? actions = GetChild(task, "Actions");
                XElement? settings = GetChild(task, "Settings");
                if (task?.Name.LocalName != "Task" || triggers == null ||
                    principals == null || actions == null || settings == null)
                {
                    return false;
                }

                List<XElement> triggerList = triggers.Elements().ToList();
                List<XElement> principalList = principals.Elements().ToList();
                List<XElement> actionList = actions.Elements().ToList();
                if (triggerList.Count != 1 || triggerList[0].Name.LocalName != "LogonTrigger" ||
                    principalList.Count != 1 || principalList[0].Name.LocalName != "Principal" ||
                    actionList.Count != 1 || actionList[0].Name.LocalName != "Exec")
                {
                    return false;
                }

                XElement trigger = triggerList[0];
                XElement principal = principalList[0];
                XElement exec = actionList[0];
                string? command = GetChildValue(exec, "Command");
                string? arguments = GetChildValue(exec, "Arguments");
                string? taskEnabled = GetChildValue(settings, "Enabled");
                string? triggerEnabled = GetChildValue(trigger, "Enabled");
                string? configuredUserSid = GetChildValue(principal, "UserId");
                string? logonType = GetChildValue(principal, "LogonType");
                string? runLevel = GetChildValue(principal, "RunLevel");

                string expectedCommand = Path.Combine(systemDirectory, "cmd.exe");
                return string.Equals(TrimOuterQuotes(command), expectedCommand, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(arguments?.Trim(), BuildScheduledTaskLauncherArguments(exePath), StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(taskEnabled, "false", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(triggerEnabled, "false", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(configuredUserSid, userSid, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(logonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(runLevel, "HighestAvailable", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsScheduledTaskCurrent(string exePath)
        {
            try
            {
                string taskPath = GetScheduledTaskPath();
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                string? userSid = identity.User?.Value;
                return ScheduledTaskExists() &&
                       !string.IsNullOrEmpty(userSid) &&
                       IsScheduledTaskDefinitionCurrent(
                           File.ReadAllText(taskPath),
                           exePath,
                           Environment.GetFolderPath(Environment.SpecialFolder.System),
                           userSid);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to inspect the auto-start task: {ex.Message}");
                return false;
            }
        }

        public static bool TrySetScheduledTask(string exePath, bool create, out string error)
        {
            error = string.Empty;
            try
            {
                if (!create && !ScheduledTaskExists())
                {
                    return true;
                }

                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(systemDirectory, "schtasks.exe"),
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "" : "runas"
                };
                foreach (string argument in BuildScheduledTaskArguments(
                             TaskName, exePath, identity.Name, systemDirectory, create))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "Task Scheduler could not be started.";
                    return false;
                }

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best-effort timeout cleanup */ }
                    error = "Task Scheduler did not finish within 30 seconds.";
                    return false;
                }

                if (!IsScheduledTaskOperationSuccessful(create, process.ExitCode, ScheduledTaskExists()))
                {
                    error = $"Task Scheduler exited with code {process.ExitCode}.";
                    AppLogger.Warn(error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Warn($"Task Scheduler configuration failed: {ex.Message}");
                return false;
            }
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

        private static bool ScheduledTaskExists()
        {
            try
            {
                _ = File.GetAttributes(GetScheduledTaskPath());
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch
            {
                // An inaccessible task must not be mistaken for a deleted task.
                return true;
            }
        }

        private static string GetScheduledTaskPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "Tasks",
                TaskName);
        }

        private static string? GetChildValue(XElement parent, string localName)
        {
            return GetChild(parent, localName)?.Value;
        }

        private static XElement? GetChild(XElement? parent, string localName)
        {
            return parent?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == localName);
        }

        private static string? TrimOuterQuotes(string? value)
        {
            string? trimmed = value?.Trim();
            return trimmed?.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
                ? trimmed[1..^1]
                : trimmed;
        }
    }
}
