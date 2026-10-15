using System.Text;
using System.Security.Principal;
using System.Xml.Linq;

namespace BASpark.Tests;

public class AutoStartManagerTests
{
    private const string Executable = @"C:\Program Files\BASpark\BASpark.exe";
    private const string UserId = "S-1-5-21-100-200-300-1001";

    private sealed class Registration : IAutoStartRegistration
    {
        public string? RunCommand { get; set; }
        public string? TaskXml { get; set; }
        internal int Registrations { get; private set; }
        internal int Deletions { get; private set; }
        internal bool FailRegistration { get; init; }
        internal bool IgnoreRegistration { get; init; }

        public void RegisterTask(string xml)
        {
            if (FailRegistration) throw new UnauthorizedAccessException();
            Registrations++;
            if (!IgnoreRegistration) TaskXml = xml;
        }

        public void DeleteTask()
        {
            Deletions++;
            TaskXml = null;
        }
    }

    [Fact]
    public void TaskVerification_AcceptsTheAccountNamesReturnedByTaskScheduler()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User!.Value;
        XDocument document = XDocument.Parse(AutoStartManager.BuildScheduledTaskXml(Executable, sid));
        XNamespace task = document.Root!.Name.Namespace;
        foreach (XElement user in document.Descendants(task + "UserId")) user.Value = identity.Name;
        Assert.True(AutoStartManager.IsScheduledTaskCurrent(document.ToString(), Executable, sid));
    }

    [Fact]
    public void Synchronize_RecreatesTheTaskRemovedByAnUninstallWithPreservedSettings()
    {
        var registration = new Registration();
        AutoStartManager.Synchronize(AutoStartManager.CreatePlan(true, true), Executable, UserId, registration);
        Assert.True(AutoStartManager.IsScheduledTaskCurrent(registration.TaskXml, Executable, UserId));
        Assert.Equal(1, registration.Registrations);
        AutoStartManager.Synchronize(AutoStartManager.CreatePlan(true, true), Executable, UserId, registration);
        Assert.Equal(1, registration.Registrations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("path")]
    [InlineData("user")]
    [InlineData("disabled")]
    [InlineData("trigger")]
    [InlineData("invalid")]
    public void Synchronize_RepairsMissingStaleDisabledAndInvalidTasks(string change)
    {
        string xml = AutoStartManager.BuildScheduledTaskXml(change == "path" ? @"D:\Old\BASpark.exe" : Executable,
            change == "user" ? "S-1-5-21-1-2-3-1001" : UserId);
        XDocument document = XDocument.Parse(xml);
        XNamespace task = document.Root!.Name.Namespace;
        if (change == "disabled") document.Root.Element(task + "Settings")!.Add(new XElement(task + "Enabled", false));
        if (change == "trigger") document.Descendants(task + "LogonTrigger").Single().Add(new XElement(task + "Enabled", false));
        var registration = new Registration { TaskXml = change == "missing" ? null : change == "invalid" ? "invalid" : document.ToString() };
        AutoStartManager.Synchronize(AutoStartManager.CreatePlan(true, true), Executable, UserId, registration);
        Assert.Equal(1, registration.Registrations);
        Assert.True(AutoStartManager.IsScheduledTaskCurrent(registration.TaskXml, Executable, UserId));
    }

    [Fact]
    public void Synchronize_RepairsNormalStartupWithoutCreatingAnElevatedTask()
    {
        var registration = new Registration { TaskXml = AutoStartManager.BuildScheduledTaskXml(Executable, UserId) };
        AutoStartManager.Synchronize(AutoStartManager.CreatePlan(true, false), Executable, UserId, registration);
        Assert.Equal(AutoStartManager.BuildRunCommand(Executable), registration.RunCommand);
        Assert.Null(registration.TaskXml);
        Assert.Equal(1, registration.Deletions);
        Assert.Equal(0, registration.Registrations);
    }

    [Fact]
    public void Synchronize_DisablingStartupRemovesBothRegistrations()
    {
        var registration = new Registration
        {
            RunCommand = AutoStartManager.BuildRunCommand(Executable),
            TaskXml = AutoStartManager.BuildScheduledTaskXml(Executable, UserId)
        };
        AutoStartManager.Synchronize(AutoStartManager.CreatePlan(false, true), Executable, UserId, registration);
        Assert.Null(registration.RunCommand);
        Assert.Null(registration.TaskXml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synchronize_FailedTaskRegistrationPreservesTheWorkingStartupEntryAndReportsFailure(bool ignored)
    {
        var registration = new Registration
        {
            RunCommand = AutoStartManager.BuildRunCommand(Executable),
            FailRegistration = !ignored,
            IgnoreRegistration = ignored
        };
        Assert.ThrowsAny<Exception>(() => AutoStartManager.Synchronize(
            AutoStartManager.CreatePlan(true, true), Executable, UserId, registration));
        Assert.Equal(AutoStartManager.BuildRunCommand(Executable), registration.RunCommand);
    }

    [Fact]
    public void CreatePlan_UsesRegistryForNormalAutoStart()
    {
        AutoStartPlan plan = AutoStartManager.CreatePlan(autoStart: true, runAsAdmin: false);

        Assert.True(plan.RegistryRunEnabled);
        Assert.False(plan.ScheduledTaskEnabled);
    }

    [Fact]
    public void CreatePlan_UsesScheduledTaskForElevatedAutoStart()
    {
        AutoStartPlan plan = AutoStartManager.CreatePlan(autoStart: true, runAsAdmin: true);

        Assert.False(plan.RegistryRunEnabled);
        Assert.True(plan.ScheduledTaskEnabled);
    }

    [Fact]
    public void CreatePlan_DisablesBothStartupMechanismsWhenAutoStartIsOff()
    {
        AutoStartPlan plan = AutoStartManager.CreatePlan(autoStart: false, runAsAdmin: true);

        Assert.False(plan.RegistryRunEnabled);
        Assert.False(plan.ScheduledTaskEnabled);
    }

    [Fact]
    public void BuildRunCommand_AddsAutostartArgumentToQuotedExecutablePath()
    {
        string command = AutoStartManager.BuildRunCommand(@"C:\Program Files\BASpark\BASpark.exe");

        Assert.Equal(@"""C:\Program Files\BASpark\BASpark.exe"" --autostart", command);
    }

    [Fact]
    public void ScheduledTask_UsesTheInteractiveUsersElevatedShellInsteadOfLaunchingUiAccessDirectly()
    {
        const string userId = "S-1-5-21-100-200-300-1001";
        XDocument document = XDocument.Parse(AutoStartManager.BuildScheduledTaskXml(@"C:\Program Files\BASpark\BASpark.exe", userId));
        XNamespace task = document.Root!.Name.Namespace;
        XElement principal = Assert.Single(document.Descendants(task + "Principal"));
        Assert.Equal(userId, principal.Element(task + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(task + "LogonType")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(task + "RunLevel")!.Value);
        Assert.Equal(userId, Assert.Single(document.Descendants(task + "LogonTrigger")).Element(task + "UserId")!.Value);
        XElement action = Assert.Single(document.Descendants(task + "Exec"));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), action.Element(task + "Command")!.Value);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.System), action.Element(task + "WorkingDirectory")!.Value);
        string arguments = action.Element(task + "Arguments")!.Value;
        Assert.StartsWith("-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ", arguments);
        string launcher = Encoding.Unicode.GetString(Convert.FromBase64String(arguments.Split(' ').Last()));
        Assert.Contains("$startInfo.UseShellExecute = $true", launcher);
        Assert.Contains("$startInfo.Arguments = '--autostart'", launcher);
        Assert.Contains("[System.Diagnostics.Process]::Start($startInfo)", launcher);
        Assert.Contains("exit 1", launcher);
        Assert.DoesNotContain("-Wait", launcher);
    }

    [Theory]
    [InlineData(@"C:\App's folder\BASpark.exe")]
    [InlineData(@"C:\测试目录\%PATH%!$(); & BASpark.exe")]
    public void ScheduledTask_PreservesExecutablePathsAsLiteralEncodedArguments(string executable)
    {
        XDocument document = XDocument.Parse(AutoStartManager.BuildScheduledTaskXml(executable, "S-1-5-21-100-200-300-1001"));
        XNamespace task = document.Root!.Name.Namespace;
        string arguments = Assert.Single(document.Descendants(task + "Arguments")).Value;
        string launcher = Encoding.Unicode.GetString(Convert.FromBase64String(arguments.Split(' ').Last()));
        Assert.Contains("$startInfo.FileName = '" + executable.Replace("'", "''") + "'", launcher);
        Assert.Contains("$startInfo.WorkingDirectory = '" + Path.GetDirectoryName(executable)!.Replace("'", "''") + "'", launcher);
    }

    [Theory]
    [InlineData("", "S-1-5-21-100-200-300-1001")]
    [InlineData(@"C:\BASpark.dll", "S-1-5-21-100-200-300-1001")]
    [InlineData(@"C:\BASpark.exe", "")]
    public void ScheduledTask_RejectsMissingExecutableOrIdentity(string executable, string userId)
    {
        Assert.Throws<ArgumentException>(() => AutoStartManager.BuildScheduledTaskXml(executable, userId));
    }

    [Fact]
    public void ResolveExecutablePath_PrefersProcessExeOverAssemblyDll()
    {
        string? resolved = AutoStartManager.ResolveExecutablePath(
            processPath: @"C:\Apps\BASpark\BASpark.exe",
            mainModulePath: @"C:\Apps\BASpark\BASpark.exe",
            assemblyLocation: @"C:\Apps\BASpark\BASpark.dll",
            baseDirectory: @"C:\Apps\BASpark");

        Assert.Equal(@"C:\Apps\BASpark\BASpark.exe", resolved);
    }
}
