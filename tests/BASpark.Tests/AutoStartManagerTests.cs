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
    public void BuildScheduledTaskArguments_UsesInteractiveHighestTokenForCurrentUser()
    {
        IReadOnlyList<string> arguments = AutoStartManager.BuildScheduledTaskArguments(
            "BASparkAutoStart",
            @"C:\Program Files\BASpark\BASpark.exe",
            @"DESKTOP\User",
            @"C:\Windows\System32",
            create: true);

        Assert.Contains(
            @"""C:\Windows\System32\cmd.exe"" /d /v:off /s /c start """" /b ""C:\Program Files\BASpark\BASpark.exe"" --autostart",
            arguments);
        Assert.Contains(@"DESKTOP\User", arguments);
        Assert.Contains("highest", arguments);
        Assert.Contains("/it", arguments);
    }

    [Fact]
    public void IsScheduledTaskDefinitionCurrent_AcceptsShellLaunchedHighestTask()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger /></Triggers>
              <Principals><Principal><UserId>S-1-5-21-123</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings />
              <Actions><Exec><Command>"C:\Windows\System32\cmd.exe"</Command><Arguments>/d /v:off /s /c start "" /b "C:\Program Files\BASpark\BASpark.exe" --autostart</Arguments></Exec></Actions>
            </Task>
            """;

        Assert.True(AutoStartManager.IsScheduledTaskDefinitionCurrent(
            xml,
            @"C:\Program Files\BASpark\BASpark.exe",
            @"C:\Windows\System32",
            "S-1-5-21-123"));
        Assert.False(AutoStartManager.IsScheduledTaskDefinitionCurrent(
            xml,
            @"C:\Program Files\BASpark\BASpark.exe",
            @"C:\Windows\System32",
            "S-1-5-21-456"));
        Assert.False(AutoStartManager.IsScheduledTaskDefinitionCurrent(
            xml.Replace("<Settings />", "<Settings><Enabled>false</Enabled></Settings>"),
            @"C:\Program Files\BASpark\BASpark.exe",
            @"C:\Windows\System32",
            "S-1-5-21-123"));
        Assert.False(AutoStartManager.IsScheduledTaskDefinitionCurrent(
            xml.Replace("</Actions>", "<Exec><Command>notepad.exe</Command></Exec></Actions>"),
            @"C:\Program Files\BASpark\BASpark.exe",
            @"C:\Windows\System32",
            "S-1-5-21-123"));
    }

    [Fact]
    public void BuildScheduledTaskLauncherArguments_RejectsEnvironmentExpansionInPath()
    {
        Assert.Throws<ArgumentException>(() =>
            AutoStartManager.BuildScheduledTaskLauncherArguments(@"C:\%TEMP%\BASpark.exe"));
    }

    [Fact]
    public void IsScheduledTaskDefinitionCurrent_RejectsDirectUiAccessExecutable()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger /></Triggers>
              <Principals><Principal><UserId>S-1-5-21-123</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings />
              <Actions><Exec><Command>"C:\Program Files\BASpark\BASpark.exe"</Command><Arguments>--autostart</Arguments></Exec></Actions>
            </Task>
            """;

        Assert.False(AutoStartManager.IsScheduledTaskDefinitionCurrent(
            xml,
            @"C:\Program Files\BASpark\BASpark.exe",
            @"C:\Windows\System32",
            "S-1-5-21-123"));
    }

    [Theory]
    [InlineData(false, 1, false, true)]
    [InlineData(false, 1, true, false)]
    [InlineData(true, 1, false, false)]
    [InlineData(true, 0, false, false)]
    [InlineData(true, 0, true, true)]
    public void IsScheduledTaskOperationSuccessful_OnlyIgnoresMissingTaskOnDelete(
        bool create, int exitCode, bool taskExists, bool expected)
    {
        Assert.Equal(
            expected,
            AutoStartManager.IsScheduledTaskOperationSuccessful(create, exitCode, taskExists));
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
