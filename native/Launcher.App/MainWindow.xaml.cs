using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;
using Launcher.Core;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string DefaultUpdateUrl = "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json";
    private const string GitHubLatestReleaseUrl = "https://api.github.com/repos/Irish-Coder69/Launcher/releases/latest";
    private static readonly HttpClient HttpClient = new();

    private readonly LauncherConfigStore _configStore = new();
    private readonly LauncherScriptBridge _scriptBridge = new();
    private readonly LauncherNativeDetectionService _nativeDetectionService = new();
    private readonly ObservableCollection<string> _logLines = new();
    private readonly ObservableCollection<StepRow> _stepRows = new();
    private bool _isBusy;

    private string _launcherRoot = string.Empty;
    private string _launcherScriptPath = string.Empty;
    private LauncherConfigDocument? _configDocument;

    public MainWindow()
    {
        InitializeComponent();
        LogListBox.ItemsSource = _logLines;
        StepsGrid.ItemsSource = _stepRows;

        DetectLauncherPaths();
        ReloadConfigView();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow(GetCurrentVersionText())
        {
            Owner = this
        };

        about.ShowDialog();

        if (about.CheckForUpdatesRequested)
        {
            await CheckForUpdatesAsync();
        }
    }

    private void DetectLauncherPaths()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateScript = Path.Combine(current.FullName, "launcher.ps1");
            var candidateConfig = Path.Combine(current.FullName, "launcher.config.json");

            if (File.Exists(candidateScript) && File.Exists(candidateConfig))
            {
                _launcherRoot = current.FullName;
                _launcherScriptPath = candidateScript;
                ConfigPathTextBox.Text = candidateConfig;
                AppendLog("Detected launcher root: " + _launcherRoot);
                return;
            }

            current = current.Parent;
        }

        var fallbackRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _launcherRoot = fallbackRoot;
        _launcherScriptPath = Path.Combine(fallbackRoot, "launcher.ps1");
        ConfigPathTextBox.Text = Path.Combine(fallbackRoot, "launcher.config.json");
    }

    private void ReloadConfigView()
    {
        _stepRows.Clear();

        try
        {
            var configPath = ConfigPathTextBox.Text.Trim();
            _configDocument = _configStore.Load(configPath);
            var config = _configDocument.Configuration;

            CheckForUpdatesCheckBox.IsChecked = config.CheckForUpdates;
            EnsureCapsLockCheckBox.IsChecked = config.EnsureCapsLockOn;
            EnsureNumLockCheckBox.IsChecked = config.EnsureNumLockOn;

            CloseOnlyTrackedCheckBox.IsChecked = config.CloseOptions.CloseOnlyTrackedApps;
            SetCloseMethodSelection(config.CloseOptions.DefaultCloseMethod);
            DefaultCloseTimeoutTextBox.Text = config.CloseOptions.DefaultCloseTimeoutSeconds.ToString();
            DefaultCloseForceCheckBox.IsChecked = config.CloseOptions.DefaultCloseForce;

            foreach (var step in config.Steps)
            {
                _stepRows.Add(new StepRow
                {
                    Name = step.Name,
                    Type = step.Type,
                    Enabled = step.Enabled,
                    ProgramPath = step.ProgramPath ?? string.Empty
                });
            }

            StatusText.Text = $"Config loaded: {config.Steps.Count} step(s)";
            AppendLog("Loaded config: " + configPath);
        }
        catch (Exception ex)
        {
            _configDocument = null;
            StatusText.Text = "Config load failed";
            AppendLog("Config error: " + ex.Message);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateUrl = ResolveUpdateUrl(GetUpdateUrl());
            AppendLog("Checking for updates from: " + updateUrl);

            var payload = await GetUpdatePayloadAsync(updateUrl);
            using var document = JsonDocument.Parse(payload);
            if (!TryGetLatestUpdatePackage(document.RootElement, out var latestPackage) || latestPackage is null)
            {
                throw new InvalidDataException("Update feed did not return a version entry.");
            }

            var currentVersionText = GetCurrentVersionText();
            if (!TryParseVersion(currentVersionText, out var currentVersion))
            {
                MessageBox.Show(this,
                    "Current version could not be parsed from version.txt.\nDetected: " + currentVersionText,
                    "Launcher Native",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (latestPackage.Version > currentVersion)
            {
                var message = "Update available.\n\nCurrent: " + currentVersion + "\nLatest: " + latestPackage.Version + "\n\nInstall now?";
                AppendLog("Update available: " + latestPackage.Version + " (current " + currentVersion + ")");

                if (string.IsNullOrWhiteSpace(latestPackage.DownloadUrl))
                {
                    MessageBox.Show(this,
                        "Update is available, but no installer download URL was provided by the feed.",
                        "Launcher Native",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var choice = MessageBox.Show(this, message, "Launcher Native", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice != MessageBoxResult.Yes)
                {
                    AppendLog("Update install was skipped by user.");
                    return;
                }

                var launched = LaunchUpdateInstaller(latestPackage.DownloadUrl, latestPackage.Checksum, out var launchError);
                if (!launched)
                {
                    AppendLog("Failed to start updater: " + launchError);
                    MessageBox.Show(this,
                        "Update installer could not be started.\n\n" + launchError,
                        "Launcher Native",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                AppendLog("Updater launched. Closing Launcher so installation can continue.");
                MessageBox.Show(this,
                    "Updater started. Launcher will close now to continue installation.",
                    "Launcher Native",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
            }
            else
            {
                AppendLog("No update available. Current version is up to date: " + currentVersion);
                MessageBox.Show(this,
                    "You are up to date.\n\nCurrent version: " + currentVersion,
                    "Launcher Native",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog("Update check failed: " + ex.Message);
            MessageBox.Show(this,
                "Update check failed.\n\n" + ex.Message,
                "Launcher Native",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool LaunchUpdateInstaller(string downloadUrl, string? checksum, out string error)
    {
        error = string.Empty;

        try
        {
            var updateScriptPath = Path.Combine(_launcherRoot, "update", "Install-LauncherUpdate.ps1");
            if (!File.Exists(updateScriptPath))
            {
                error = "Install-LauncherUpdate.ps1 was not found: " + updateScriptPath;
                return false;
            }

            var hostExecutable = ResolvePowerShellHost();
            var arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{updateScriptPath}\" -DownloadUrl \"{downloadUrl}\" -InstallDir \"{_launcherRoot}\"";

            if (!string.IsNullOrWhiteSpace(checksum))
            {
                arguments += $" -Checksum \"{checksum}\"";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = hostExecutable,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = _launcherRoot
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ResolvePowerShellHost()
    {
        var pwshPath = FindExecutableInPath("pwsh.exe");
        if (!string.IsNullOrWhiteSpace(pwshPath))
        {
            return pwshPath;
        }

        var powershellPath = FindExecutableInPath("powershell.exe");
        if (!string.IsNullOrWhiteSpace(powershellPath))
        {
            return powershellPath;
        }

        throw new FileNotFoundException("PowerShell host was not found. Install PowerShell 7 or Windows PowerShell.");
    }

    private static string? FindExecutableInPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var pathPart in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(pathPart))
            {
                continue;
            }

            var candidate = Path.Combine(pathPart.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string> GetUpdatePayloadAsync(string updateUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, updateUrl);
        request.Headers.UserAgent.ParseAdd("Launcher-Native");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static string ResolveUpdateUrl(string updateUrl)
    {
        if (TryResolveGitHubVersionsFeed(updateUrl, out var resolvedUrl))
        {
            return resolvedUrl;
        }

        return updateUrl;
    }

    private static bool TryResolveGitHubVersionsFeed(string updateUrl, out string resolvedUrl)
    {
        resolvedUrl = updateUrl;

        if (string.IsNullOrWhiteSpace(updateUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return false;
        }

        if (!string.Equals(segments[3], "update", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(segments[^1], "versions.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolvedUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest";
        return true;
    }

    private static bool TryGetLatestUpdatePackage(JsonElement rootElement, out UpdatePackage? package)
    {
        package = null;

        if (rootElement.ValueKind == JsonValueKind.Array)
        {
            UpdatePackage? arrayLatestPackage = null;

            foreach (var item in rootElement.EnumerateArray())
            {
                if (!TryGetVersionText(item, out var versionText))
                {
                    continue;
                }

                if (!TryParseVersion(versionText, out var parsedVersion))
                {
                    continue;
                }

                if (arrayLatestPackage is null || parsedVersion > arrayLatestPackage.Version)
                {
                    package = new UpdatePackage(
                        parsedVersion,
                        TryGetStringProperty(item, "downloadUrl"),
                        TryNormalizeSha256(TryGetStringProperty(item, "checksum")));
                    arrayLatestPackage = package;
                }
            }

            if (arrayLatestPackage is null)
            {
                return false;
            }

            package = arrayLatestPackage;
            return true;
        }

        if (rootElement.ValueKind == JsonValueKind.Object && TryGetVersionText(rootElement, out var objectVersionText))
        {
            if (!TryParseVersion(objectVersionText, out var parsedVersion))
            {
                return false;
            }

            var downloadUrl = TryGetStringProperty(rootElement, "downloadUrl");
            string? checksum = TryNormalizeSha256(TryGetStringProperty(rootElement, "checksum"));

            if (rootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var candidateUrl = TryGetStringProperty(asset, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(candidateUrl))
                    {
                        continue;
                    }

                    if (candidateUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = candidateUrl;
                        var digest = TryGetStringProperty(asset, "digest");
                        if (!string.IsNullOrWhiteSpace(digest))
                        {
                            checksum = TryNormalizeSha256(digest);
                        }
                        break;
                    }
                }
            }

            package = new UpdatePackage(parsedVersion, downloadUrl, checksum);
            return true;
        }

        return false;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryNormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring("sha256:".Length);
        }

        return trimmed;
    }

    private sealed record UpdatePackage(Version Version, string? DownloadUrl, string? Checksum);

    private static bool TryGetVersionText(JsonElement element, out string? versionText)
    {
        versionText = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "version", "tag_name", "tagName" })
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            var candidate = property.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                versionText = candidate;
                return true;
            }
        }

        return false;
    }

    private string GetCurrentVersionText()
    {
        var versionPath = Path.Combine(_launcherRoot, "version.txt");
        if (!File.Exists(versionPath))
        {
            return "unknown";
        }

        try
        {
            var text = File.ReadAllText(versionPath).Trim();
            return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
        }
        catch
        {
            return "unknown";
        }
    }

    private string GetUpdateUrl()
    {
        try
        {
            var node = _configDocument?.Root?["updateCheckUrl"];
            if (node is not null)
            {
                var text = node.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch
        {
            // Fall back to the default feed URL.
        }

        return DefaultUpdateUrl;
    }

    private static bool TryParseVersion(string? versionText, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var normalized = versionText.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (!Version.TryParse(normalized, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private void SetCloseMethodSelection(string? closeMethod)
    {
        var method = string.IsNullOrWhiteSpace(closeMethod) ? "both" : closeMethod;
        foreach (var item in DefaultCloseMethodComboBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboItem)
            {
                var value = comboItem.Content?.ToString() ?? string.Empty;
                if (string.Equals(value, method, StringComparison.OrdinalIgnoreCase))
                {
                    DefaultCloseMethodComboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }

        DefaultCloseMethodComboBox.SelectedIndex = 0;
    }

    private string GetSelectedCloseMethod()
    {
        if (DefaultCloseMethodComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem comboItem)
        {
            return comboItem.Content?.ToString() ?? "both";
        }

        return "both";
    }

    private async Task RunLauncherModeAsync(LauncherMode mode)
    {
        if (_isBusy)
        {
            return;
        }

        var configPath = ConfigPathTextBox.Text.Trim();
        if (!File.Exists(_launcherScriptPath))
        {
            MessageBox.Show(this, "launcher.ps1 was not found. Verify launcher root detection.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!File.Exists(configPath))
        {
            MessageBox.Show(this, "Config file was not found.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusyState(true);
        var dryRun = DryRunCheckBox.IsChecked == true;

        AppendLog($"Running mode {mode} (DryRun={dryRun})");
        StatusText.Text = "Running...";

        try
        {
            var exitCode = await _scriptBridge.RunAsync(
                _launcherScriptPath,
                configPath,
                mode,
                dryRun,
                line => Dispatcher.Invoke(() => AppendLog(line)));

            StatusText.Text = exitCode == 0 ? "Completed" : "Completed with errors";
            AppendLog("Exit code: " + exitCode);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Run failed";
            AppendLog("Run error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        _isBusy = isBusy;
        StartButton.IsEnabled = !isBusy;
        CloseButton.IsEnabled = !isBusy;
        StartAndWaitButton.IsEnabled = !isBusy;
        DetectRunningButton.IsEnabled = !isBusy;
        DetectCloseTargetsButton.IsEnabled = !isBusy;
        ReloadConfigButton.IsEnabled = !isBusy;
        BrowseConfigButton.IsEnabled = !isBusy;
        SaveSettingsButton.IsEnabled = !isBusy;
        ResetSettingsButton.IsEnabled = !isBusy;
        StepsGrid.IsEnabled = !isBusy;
    }

    private void AppendLog(string message)
    {
        _logLines.Add(message);

        while (_logLines.Count > 1200)
        {
            _logLines.RemoveAt(0);
        }

        if (_logLines.Count > 0)
        {
            LogListBox.ScrollIntoView(_logLines[_logLines.Count - 1]);
        }
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunLauncherModeAsync(LauncherMode.Start);
    }

    private async void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunLauncherModeAsync(LauncherMode.Close);
    }

    private void StartAndWaitButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var configPath = ConfigPathTextBox.Text.Trim();
        if (!File.Exists(_launcherScriptPath) || !File.Exists(configPath))
        {
            MessageBox.Show(this, "launcher.ps1 or config path is invalid.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var dryRun = DryRunCheckBox.IsChecked == true;
            _scriptBridge.LaunchInteractiveStartAndWait(_launcherScriptPath, configPath, dryRun);
            StatusText.Text = "Interactive window opened";
            AppendLog("Opened interactive StartAndWait session in a separate PowerShell window.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Interactive launch failed";
            AppendLog("Interactive launch error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadConfigButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReloadConfigView();
    }

    private void BrowseConfigButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Launcher Config|*.json|All Files|*.*",
            CheckFileExists = true,
            InitialDirectory = File.Exists(ConfigPathTextBox.Text)
                ? Path.GetDirectoryName(ConfigPathTextBox.Text)
                : _launcherRoot
        };

        if (dialog.ShowDialog(this) == true)
        {
            ConfigPathTextBox.Text = dialog.FileName;
            ReloadConfigView();
        }
    }

    private bool TryGetSelectedStep(out LauncherStep? step)
    {
        step = null;

        if (_configDocument is null)
        {
            return false;
        }

        if (StepsGrid.SelectedItem is not StepRow row)
        {
            MessageBox.Show(this, "Select a step first.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        step = _configDocument.Configuration.Steps.FirstOrDefault(s => string.Equals(s.Name, row.Name, StringComparison.Ordinal));
        if (step is null)
        {
            MessageBox.Show(this, "Selected step was not found in config.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void DetectRunningButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedStep(out var step) || step is null)
        {
            return;
        }

        var isRunning = _nativeDetectionService.IsStepRunning(step);
        AppendLog($"Native detection for '{step.Name}': Running={isRunning}");
        StatusText.Text = isRunning ? "Selected step appears running" : "Selected step appears not running";
    }

    private void DetectCloseTargetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedStep(out var step) || step is null)
        {
            return;
        }

        var targets = _nativeDetectionService.FindCloseTargets(step);
        if (targets.Count == 0)
        {
            AppendLog($"No close targets found for '{step.Name}'.");
            StatusText.Text = "No close targets found";
            return;
        }

        var summary = string.Join(", ", targets.Select(t => t.ProcessName + "#" + t.Id));
        AppendLog($"Close targets for '{step.Name}': {summary}");
        StatusText.Text = $"Found {targets.Count} close target(s)";
    }

    private void SaveSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_configDocument is null)
        {
            MessageBox.Show(this, "No config is loaded.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DefaultCloseTimeoutTextBox.Text.Trim(), out var timeoutSeconds) || timeoutSeconds < 1)
        {
            MessageBox.Show(this, "Default close timeout must be a positive integer.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new LauncherSettingsInput
        {
            CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
            EnsureCapsLockOn = EnsureCapsLockCheckBox.IsChecked == true,
            EnsureNumLockOn = EnsureNumLockCheckBox.IsChecked == true,
            CloseOnlyTrackedApps = CloseOnlyTrackedCheckBox.IsChecked == true,
            DefaultCloseMethod = GetSelectedCloseMethod(),
            DefaultCloseTimeoutSeconds = timeoutSeconds,
            DefaultCloseForce = DefaultCloseForceCheckBox.IsChecked == true
        };

        var enabledByName = _stepRows.ToDictionary(s => s.Name, s => s.Enabled, StringComparer.Ordinal);

        try
        {
            _configStore.ApplyGlobalSettings(_configDocument, settings);
            _configStore.ApplyStepEnabledStates(_configDocument, enabledByName);
            _configStore.Save(_configDocument);
            AppendLog("Saved config changes to " + _configDocument.FilePath);
            StatusText.Text = "Config saved";

            ReloadConfigView();
        }
        catch (Exception ex)
        {
            AppendLog("Save failed: " + ex.Message);
            StatusText.Text = "Save failed";
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReloadConfigView();
        AppendLog("Reloaded config without saving edits.");
    }

    private sealed class StepRow
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string ProgramPath { get; set; } = string.Empty;
    }
}