namespace BASpark.Tests;

public sealed class InstallerSessionTests
{
    private static InstallerChoices Choices(string? directory = null) => new(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BASpark"),
        "BASpark", true, false, false, true);

    [Fact]
    public void InstallerMode_IsExplicitAndDoesNotInterceptNormalLaunches()
    {
        Assert.False(InstallerSession.TryCreate([], out _));
        Assert.False(InstallerSession.TryCreate(["--autostart"], out _));
        Assert.True(InstallerSession.TryCreate(["--uninstaller", "--backend=C:\\BASpark\\unins000.exe",
            "--install-dir=C:\\BASpark"], out InstallerSession? session));
        Assert.True(session!.IsUninstall);
        Assert.Equal("C:\\BASpark\\unins000.exe", session.BackendPath);
        Assert.Equal("C:\\BASpark", session.Directory);
    }

    [Fact]
    public void UiAccessInstallation_RequiresExplicitConsentAndASecureLocation()
    {
        var session = new InstallerSession(false, true, "setup.exe");
        InstallerChoices choices = Choices();
        Assert.Equal("Installer_CertificateRequired", session.Validate(choices));
        Assert.Null(session.Validate(choices with { TrustCertificate = true }));
        Assert.Equal("Installer_SecureDirectoryRequired", session.Validate(
            choices with { TrustCertificate = true, Directory = Path.Combine(Path.GetTempPath(), "BASpark") }));
    }

    [Fact]
    public void DirectoryValidation_NormalizesParentSegmentsBeforeCheckingProgramFiles()
    {
        var session = new InstallerSession(false, true, "setup.exe");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal("Installer_SecureDirectoryRequired", session.Validate(
            Choices(Path.Combine(programFiles, "..", "BASpark")) with { TrustCertificate = true }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\BASpark")]
    [InlineData("C:\\BASpark\"invalid")]
    public void Installation_RejectsInvalidDirectories(string directory)
    {
        Assert.Equal("Installer_InvalidDirectory", new InstallerSession(false, false, "setup.exe").Validate(Choices(directory)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\Outside")]
    [InlineData("C:\\Outside")]
    public void StartMenuValidation_AppliesOnlyWhenTheShortcutIsEnabled(string folder)
    {
        var session = new InstallerSession(false, false, "setup.exe");
        InstallerChoices choices = Choices() with { StartMenuFolder = folder };
        Assert.Equal("Installer_InvalidStartMenu", session.Validate(choices));
        Assert.Null(session.Validate(choices with { CreateStartMenu = false }));
    }

    [Theory]
    [InlineData("zh-CN", "chinesesimplified")]
    [InlineData("en", "english")]
    [InlineData("ja", "japanese")]
    public void InstallWorker_ReceivesTheSelectedOptionsWithoutInteractiveDialogs(string culture, string innoLanguage)
    {
        var session = new InstallerSession(false, true, "setup.exe");
        string[] arguments = session.BuildArguments(Choices() with
        {
            CreateStartMenu = false, CreateDesktopShortcut = true, TrustCertificate = true
        }, culture, "C:\\Temp\\progress.txt", "C:\\Temp\\operation.log");
        Assert.Contains("/VERYSILENT", arguments);
        Assert.Contains("/SUPPRESSMSGBOXES", arguments);
        Assert.Contains("/NOICONS", arguments);
        Assert.Contains("/ACCEPTUIACCESSCERT=1", arguments);
        Assert.Contains("/TASKS=desktopicon", arguments);
        Assert.Contains("/LANG=" + innoLanguage, arguments);
    }

    [Fact]
    public void InstallWorker_DoesNotImplicitlyAcceptCertificateTrustOrDesktopShortcuts()
    {
        string[] arguments = new InstallerSession(false, true, "setup.exe").BuildArguments(
            Choices(), "zh-CN", "progress.txt", "operation.log");
        Assert.DoesNotContain("/ACCEPTUIACCESSCERT=1", arguments);
        Assert.Contains("/TASKS=", arguments);
    }

    [Theory]
    [InlineData(true, "/BASPARKKEEPDATA=1")]
    [InlineData(false, "/BASPARKKEEPDATA=0")]
    public void UninstallWorker_ReceivesTheUserDataChoiceWithoutInstallationOptions(bool keep, string expected)
    {
        string[] arguments = new InstallerSession(true, false, "unins000.exe").BuildArguments(
            Choices() with { KeepUserData = keep }, "zh-CN", "progress.txt", "operation.log");
        Assert.Contains(expected, arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("/DIR=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("/TASKS=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("正在提取文件...", "C:\\BASpark\\app.dll")]
    [InlineData("Extracting files...", "C:\\BASpark\\app.dll")]
    [InlineData("ファイルを展開しています...", "C:\\BASpark\\app.dll")]
    public void Output_UsesTheNativeInstallerStatusAndFilename(string status, string file)
    {
        InstallerProgress update = Assert.IsType<InstallerProgress>(new InstallerProgressReader().Read($"25\r\n{status}\r\n{file}\r\n"));
        Assert.Equal(25, update.Percentage);
        Assert.Equal(status + Environment.NewLine + file, update.Message);
    }

    [Fact]
    public void Output_ReportsOnlyChangedProgressAndMessages()
    {
        var reader = new InstallerProgressReader();
        Assert.NotNull(reader.Read("10\nExtracting files...\nfirst.dll\n"));
        Assert.Null(reader.Read("10\nExtracting files...\nfirst.dll\n"));
        InstallerProgress percentage = Assert.IsType<InstallerProgress>(reader.Read("11\nExtracting files...\nfirst.dll\n"));
        Assert.Equal(11, percentage.Percentage);
        Assert.Null(percentage.Message);
        InstallerProgress file = Assert.IsType<InstallerProgress>(reader.Read("11\nExtracting files...\nsecond.dll\n"));
        Assert.Null(file.Percentage);
        Assert.Equal("second.dll", file.Message);
        InstallerProgress phase = Assert.IsType<InstallerProgress>(reader.Read("90\nCreating shortcuts...\n\n"));
        Assert.Equal("Creating shortcuts...", phase.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25\r\nExtracting files...\r\nfirst.dll")]
    [InlineData("invalid\r\nExtracting files...\r\nfirst.dll\r\n")]
    public void Output_DoesNotConsumePartiallyWrittenSnapshots(string incomplete)
    {
        var reader = new InstallerProgressReader();
        Assert.Null(reader.Read(incomplete));
        Assert.NotNull(reader.Read("25\r\nExtracting files...\r\nfirst.dll\r\n"));
    }

    [Fact]
    public void UninstallOutput_ShowsTheNativeStatusOnce()
    {
        var reader = new InstallerProgressReader();
        InstallerProgress initial = Assert.IsType<InstallerProgress>(reader.Read("10\nRemoving BASpark...\n\n"));
        Assert.Equal("Removing BASpark...", initial.Message);
        InstallerProgress next = Assert.IsType<InstallerProgress>(reader.Read("90\nRemoving BASpark...\n\n"));
        Assert.Equal(90, next.Percentage);
        Assert.Null(next.Message);
    }
}
