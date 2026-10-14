using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using Microsoft.Win32;

namespace BASpark;

internal sealed record InstallerChoices(string Directory, string StartMenuFolder, bool CreateStartMenu,
    bool CreateDesktopShortcut, bool TrustCertificate, bool KeepUserData);

internal sealed record InstallerProgress(double? Percentage, string? Message);

internal interface IInstallerBackend
{
    Task RunAsync(InstallerChoices choices, string culture, IProgress<InstallerProgress> progress);
}

internal sealed class InstallerProgressReader
{
    private int? _percentage;
    private string _status = "";
    private string _file = "";

    internal InstallerProgress? Read(string snapshot)
    {
        string[] lines = snapshot.Split('\n');
        if (lines.Length != 4 || lines[3].Length != 0 ||
            !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int percentage)) return null;
        percentage = Math.Clamp(percentage, 0, 100);
        string status = lines[1].TrimEnd('\r');
        string file = lines[2].TrimEnd('\r');
        var messages = new List<string>();
        if (status.Length > 0 && status != _status) messages.Add(status);
        if (file.Length > 0 && file != _file) messages.Add(file);
        double? changedPercentage = percentage != _percentage ? percentage : null;
        _percentage = percentage;
        _status = status;
        _file = file;
        return changedPercentage != null || messages.Count > 0
            ? new InstallerProgress(changedPercentage, messages.Count > 0 ? string.Join(Environment.NewLine, messages) : null)
            : null;
    }
}

internal sealed class InstallerSession : IInstallerBackend
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B0A2C1D4-E3F5-4A6B-9C8D-7E1F2A3B4C5D}_is1";
    public bool IsUninstall { get; }
    public bool RequiresCertificate { get; }
    public bool IsUpdate { get; }
    public string Directory { get; }
    public string BackendPath { get; }
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    internal InstallerSession(bool uninstall, bool certificate, string backend, string? directory = null,
        bool update = false)
    {
        IsUninstall = uninstall;
        RequiresCertificate = certificate;
        BackendPath = backend;
        Directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BASpark");
        IsUpdate = update;
    }

    public static bool TryCreate(string[] arguments, out InstallerSession? session)
    {
        session = null;
        bool uninstall = arguments.Contains("--uninstaller", StringComparer.OrdinalIgnoreCase);
        if (!uninstall && !arguments.Contains("--installer", StringComparer.OrdinalIgnoreCase)) return false;
        string Read(string name) => arguments.FirstOrDefault(argument =>
            argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..] ?? "";
        string backend = Read("--backend");
        string directory = Read("--install-dir");
        bool update = false;
        if (!uninstall)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? installed = machine.OpenSubKey(UninstallKey);
                if (installed?.GetValue("InstallLocation") is not string previous || string.IsNullOrWhiteSpace(previous)) continue;
                directory = previous;
                update = true;
                break;
            }
        }
        session = new InstallerSession(uninstall, Read("--uiaccess") == "1", backend,
            string.IsNullOrWhiteSpace(directory) ? null : directory, update);
        return true;
    }

    public string? Validate(InstallerChoices choices)
    {
        if (IsUninstall) return null;
        if (!Path.IsPathFullyQualified(choices.Directory) || choices.Directory.Contains('"') ||
            choices.Directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "Installer_InvalidDirectory";
        if (RequiresCertificate)
        {
            if (!choices.TrustCertificate) return "Installer_CertificateRequired";
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(choices.Directory));
            if (!normalized.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Installer_SecureDirectoryRequired";
        }
        if (choices.CreateStartMenu && (string.IsNullOrWhiteSpace(choices.StartMenuFolder) ||
            Path.IsPathRooted(choices.StartMenuFolder) || choices.StartMenuFolder.Contains("..", StringComparison.Ordinal) ||
            choices.StartMenuFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return "Installer_InvalidStartMenu";
        return null;
    }

    internal string[] BuildArguments(InstallerChoices choices, string culture, string statusPath, string logPath)
    {
        var arguments = new List<string>
        {
            "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/BASPARKWORKER=1",
            "/BASPARKSTATUS=" + statusPath, "/LOG=" + logPath,
            "/BASPARKKEEPDATA=" + (choices.KeepUserData ? "1" : "0")
        };
        if (!IsUninstall)
        {
            arguments.Add("/LANG=" + (culture == "zh-CN" ? "chinesesimplified" : culture == "ja" ? "japanese" : "english"));
            arguments.Add("/DIR=" + choices.Directory);
            arguments.Add("/GROUP=" + choices.StartMenuFolder);
            arguments.Add("/TASKS=" + (choices.CreateDesktopShortcut ? "desktopicon" : ""));
            if (!choices.CreateStartMenu) arguments.Add("/NOICONS");
            if (choices.TrustCertificate) arguments.Add("/ACCEPTUIACCESSCERT=1");
        }
        return arguments.ToArray();
    }

    public async Task RunAsync(InstallerChoices choices, string culture, IProgress<InstallerProgress> progress)
    {
        string? invalid = Validate(choices);
        if (invalid != null) throw new InvalidOperationException(Localization.Get(invalid, culture));
        if (!File.Exists(BackendPath)) throw new FileNotFoundException(Localization.Get("Installer_BackendMissing", culture), BackendPath);
        string work = Path.Combine(Path.GetTempPath(), "BASpark.Setup", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(work);
        string statusPath = Path.Combine(work, "progress.txt");
        string logPath = Path.Combine(work, "operation.log");
        var start = new ProcessStartInfo(BackendPath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) start.Verb = "runas";
        foreach (string argument in BuildArguments(choices, culture, statusPath, logPath)) start.ArgumentList.Add(argument);
        using Process worker = Process.Start(start) ?? throw new InvalidOperationException(Localization.Get("Installer_BackendMissing", culture));
        var progressReader = new InstallerProgressReader();
        async Task ReadOutputAsync()
        {
            try
            {
                if (File.Exists(statusPath) && progressReader.Read(await File.ReadAllTextAsync(statusPath)) is { } update)
                    progress.Report(update);
            }
            catch (IOException) { }
        }
        while (!worker.HasExited)
        {
            await ReadOutputAsync();
            await Task.Delay(80);
        }
        await worker.WaitForExitAsync();
        await ReadOutputAsync();
        if (worker.ExitCode is not (0 or 3010))
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                Localization.Get("Installer_BackendFailed", culture), worker.ExitCode));
        progress.Report(new(100, Localization.Get(IsUninstall ? "Installer_UninstallComplete" : "Installer_InstallComplete", culture)));
    }
}
