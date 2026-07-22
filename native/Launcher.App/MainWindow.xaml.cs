using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
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
            var updateUrl = GetUpdateUrl();
            AppendLog("Checking for updates from: " + updateUrl);

            var payload = await HttpClient.GetStringAsync(updateUrl);
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Update feed did not return a version list.");
            }

            Version? latestVersion = null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("version", out var versionProperty))
                {
                    continue;
                }

                var versionText = versionProperty.GetString();
                if (!TryParseVersion(versionText, out var parsedVersion))
                {
                    continue;
                }

                if (latestVersion is null || parsedVersion > latestVersion)
                {
                    latestVersion = parsedVersion;
                }
            }

            if (latestVersion is null)
            {
                throw new InvalidDataException("No valid versions were found in update feed.");
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

            if (latestVersion > currentVersion)
            {
                var message = "Update available.\n\nCurrent: " + currentVersion + "\nLatest: " + latestVersion;
                AppendLog("Update available: " + latestVersion + " (current " + currentVersion + ")");
                MessageBox.Show(this, message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
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