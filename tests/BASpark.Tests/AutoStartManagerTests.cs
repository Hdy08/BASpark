using System.Text;
using System.Xml.Linq;

namespace BASpark.Tests;

public class AutoStartManagerTests
{
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
