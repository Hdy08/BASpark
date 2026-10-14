using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace BASpark;

internal sealed record InstallerChoices(string Directory, string StartMenuFolder, bool CreateStartMenu,
    bool CreateDesktopShortcut, bool TrustCertificate, bool KeepUserData);

internal sealed record InstallerProgress(double? Percentage, string? Message);

internal interface IInstallerBackend
{
    Task RunAsync(InstallerChoices choices, string culture, IProgress<InstallerProgress> progress);
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

    internal static string FormatOutputLine(string line, DateTime timestamp)
    {
        if (line.Length > 23 && char.IsWhiteSpace(line[23]) &&
            DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime originalTimestamp))
        {
            timestamp = originalTimestamp;
            line = line[23..].TrimStart();
        }
        return $"[{timestamp:HH:mm:ss.fff}] [INFO] {line.TrimEnd('\r')}";
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
        long logPosition = 0;
        string pending = "";
        async Task ReadOutputAsync()
        {
            try
            {
                if (File.Exists(statusPath) && double.TryParse(await File.ReadAllTextAsync(statusPath),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out double percentage))
                    progress.Report(new(Math.Clamp(percentage, 0, 100), null));
                if (!File.Exists(logPath)) return;
                await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(logPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: logPosition == 0);
                string text = pending + await reader.ReadToEndAsync();
                logPosition = stream.Position;
                int lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0) { pending = text; return; }
                string[] lines = text[..lastNewline].Split('\n').Select(line => line.TrimEnd('\r'))
                    .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                if (lines.Length > 0) progress.Report(new(null, string.Join(Environment.NewLine, lines)));
                pending = text[(lastNewline + 1)..];
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
        if (pending.Length > 0) progress.Report(new(null, pending));
        if (worker.ExitCode is not (0 or 3010))
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                Localization.Get("Installer_BackendFailed", culture), worker.ExitCode));
        progress.Report(new(100, Localization.Get(IsUninstall ? "Installer_UninstallComplete" : "Installer_InstallComplete", culture)));
    }
}
