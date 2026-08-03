using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
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
    private const string TaughtStepMarker = "[TaughtFlow]";

    private readonly LauncherConfigStore _configStore = new();
    private readonly LauncherScriptBridge _scriptBridge = new();
    private readonly LauncherNativeDetectionService _nativeDetectionService = new();
    private readonly LauncherNativeStartRunner _nativeStartRunner = new();
    private readonly LauncherLearningService _learningService = new();
    private readonly LauncherSecretStoreService _secretStore = new();
    private readonly LauncherProgramInventoryService _programInventoryService = new();
    private readonly ObservableCollection<string> _logLines = new();
    private readonly ObservableCollection<StepRow> _stepRows = new();
    private readonly ObservableCollection<string> _recommendedOrderLines = new();
    private readonly ObservableCollection<string> _secretNames = new();
    private readonly ObservableCollection<ProgramSearchResult> _programSearchResults = new();
    private readonly List<ProgramSearchResult> _teachSessionCapturedApps = new();
    private readonly HashSet<string> _teachSessionCapturedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TeachCapturedProgram> _teachSessionPrograms = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TeachFocusEvent> _teachSessionFocusEvents = new();
    private readonly List<TeachInteractionEvent> _teachSessionInteractionEvents = new();
    private readonly object _teachSessionEventLock = new();
    private bool _teachSessionCaptureSensitiveInput;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private HookProc? _keyboardHookProc;
    private HookProc? _mouseHookProc;
    private bool _isBusy;
    private bool _isTeachSessionActive;
    private HashSet<int> _teachSessionBaselineProcessIds = new();
    private DispatcherTimer? _teachSessionTimer;
    private CancellationTokenSource? _runCancellationSource;
    private readonly LauncherUserProfile? _currentUser;

    private string _launcherRoot = string.Empty;
    private string _launcherScriptPath = string.Empty;
    private LauncherConfigDocument? _configDocument;

    public MainWindow(LauncherUserProfile? currentUser = null)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        LogListBox.ItemsSource = _logLines;
        StepsGrid.ItemsSource = _stepRows;
        RecommendedOrderListBox.ItemsSource = _recommendedOrderLines;
        SecretsListBox.ItemsSource = _secretNames;
        ProgramSearchResultsListBox.ItemsSource = _programSearchResults;

        DetectLauncherPaths();
        ReloadConfigView();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopTeachSessionHooks();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        CurrentUserTextBlock.Text = _currentUser is null
            ? "Signed in user: none"
            : $"Signed in user: {_currentUser.DisplayName} ({_currentUser.UserName})";

        await WarmProgramInventoryAsync();
    }

    private async Task WarmProgramInventoryAsync()
    {
        try
        {
            var cached = _programInventoryService.GetCachedPrograms();
            if (cached.Count > 0)
            {
                AppendLog($"Program inventory cache loaded ({cached.Count} entries).");
                return;
            }

            var entries = await _programInventoryService.GetProgramsAsync();
            AppendLog($"Program inventory scanned ({entries.Count} entries). Cache: {_programInventoryService.CacheFilePath}");
        }
        catch (Exception ex)
        {
            AppendLog("Program inventory warm-up failed: " + ex.Message);
        }
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow(GetCurrentVersionText())
        {
            Owner = this
        };

        about.ShowDialog();
    }

    private void CheckForUpdatesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var updates = new UpdatesWindow(GetCurrentVersionText(), GetUpdateUrl())
        {
            Owner = this
        };

        updates.ShowDialog();
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

            LearningEnabledCheckBox.IsChecked = config.Learning.Enabled;
            ShowRecommendedOrderCheckBox.IsChecked = config.Learning.ShowRecommendedOrder;
            AutoApplyRecommendedOrderCheckBox.IsChecked = config.Learning.AutoApplyRecommendedOrder;
            MinRunsBeforeSuggestionsTextBox.Text = Math.Max(1, config.Learning.MinRunsBeforeSuggestions).ToString();
            UseLearnedOrderThisRunCheckBox.IsChecked = config.Learning.AutoApplyRecommendedOrder;

            foreach (var step in GetTaughtSteps(config).Where(step => step.Enabled && string.Equals(step.Type, "launch", StringComparison.OrdinalIgnoreCase)))
            {
                _stepRows.Add(new StepRow
                {
                    Name = step.Name,
                    Type = step.Type,
                    Enabled = step.Enabled,
                    ProgramPath = step.ProgramPath ?? string.Empty
                });
            }

            StatusText.Text = _stepRows.Count == 0
                ? "Run tab is blank until programs are taught"
                : $"Taught flow loaded: {_stepRows.Count} step(s)";
            AppendLog("Loaded config: " + configPath);
            RefreshLearningRecommendations();
            RefreshSecretList();
            ApplyTeachSessionButton.IsEnabled = !_isBusy && !_isTeachSessionActive && _teachSessionCapturedApps.Count > 0;
        }
        catch (Exception ex)
        {
            _configDocument = null;
            StatusText.Text = "Config load failed";
            AppendLog("Config error: " + ex.Message);
            _recommendedOrderLines.Clear();
            RecommendedOrderStatusText.Text = "Recommendation unavailable because config could not be loaded.";
            _secretNames.Clear();
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
        if (mode != LauncherMode.Start && !File.Exists(_launcherScriptPath))
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

        _runCancellationSource = new CancellationTokenSource();
        var cancellationToken = _runCancellationSource.Token;

        try
        {
            if (mode == LauncherMode.Start)
            {
                if (_configDocument is null || !string.Equals(_configDocument.FilePath, configPath, StringComparison.OrdinalIgnoreCase))
                {
                    _configDocument = _configStore.Load(configPath);
                }

                var taughtSteps = GetTaughtSteps(_configDocument.Configuration)
                    .Where(step => step.Enabled && string.Equals(step.Type, "launch", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (taughtSteps.Count == 0)
                {
                    StatusText.Text = "No taught steps yet";
                    AppendLog("Run Start skipped: no taught startup steps are saved yet.");
                    MessageBox.Show(
                        this,
                        "No taught startup steps exist yet.\n\nUse Start Teach Session, open/login to your apps, then click Apply Taught Flow.",
                        "Launcher Native",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var runtimeDocument = BuildRuntimeDocumentForTaughtSteps(_configDocument, taughtSteps);

                await _nativeStartRunner.RunAsync(
                    runtimeDocument,
                    dryRun,
                    line => Dispatcher.Invoke(() => AppendLog(line)),
                    UseLearnedOrderThisRunCheckBox.IsChecked == true,
                    cancellationToken);

                StatusText.Text = "Completed";
                AppendLog("Native start run completed.");
                RefreshLearningRecommendations();
            }
            else
            {
                var exitCode = await _scriptBridge.RunAsync(
                    _launcherScriptPath,
                    configPath,
                    mode,
                    dryRun,
                    line => Dispatcher.Invoke(() => AppendLog(line)),
                    cancellationToken);

                StatusText.Text = exitCode == 0 ? "Completed" : "Completed with errors";
                AppendLog("Exit code: " + exitCode);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped";
            AppendLog("Run stopped by user.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Run failed";
            AppendLog("Run error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _runCancellationSource?.Dispose();
            _runCancellationSource = null;
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
        ResetLearningHistoryButton.IsEnabled = !isBusy;
        UseLearnedOrderThisRunCheckBox.IsEnabled = !isBusy;
        AddOrUpdateProgramStepButton.IsEnabled = !isBusy;
        PopulateBuilderFromSelectedStepButton.IsEnabled = !isBusy;
        ClearProgramBuilderButton.IsEnabled = !isBusy;
        SearchProgramsButton.IsEnabled = !isBusy;
        ApplyProgramSearchResultButton.IsEnabled = !isBusy;
        LearnProgramsIntoSearchButton.IsEnabled = !isBusy;
        RefreshInventoryButton.IsEnabled = !isBusy;
        LearnOpenAppsButton.IsEnabled = !isBusy;
        StartTeachSessionButton.IsEnabled = !isBusy && !_isTeachSessionActive;
        StopTeachSessionButton.IsEnabled = !isBusy && _isTeachSessionActive;
        ApplyTeachSessionButton.IsEnabled = !isBusy && !_isTeachSessionActive && _teachSessionCapturedApps.Count > 0;
        ProgramSearchQueryTextBox.IsEnabled = !isBusy;
        ProgramSearchResultsListBox.IsEnabled = !isBusy;
        SaveProgramSecretButton.IsEnabled = !isBusy;
        InsertSecretTokenButton.IsEnabled = !isBusy;
        RefreshSecretsButton.IsEnabled = !isBusy;
        RenameSecretButton.IsEnabled = !isBusy;
        DeleteSecretButton.IsEnabled = !isBusy;
        RenameSecretNameTextBox.IsEnabled = !isBusy;
        SecretsListBox.IsEnabled = !isBusy;
        ProgramSecretNameTextBox.IsEnabled = !isBusy;
        ProgramSecretValuePasswordBox.IsEnabled = !isBusy;
        StepsGrid.IsEnabled = !isBusy;
        StopButton.IsEnabled = isBusy;
    }

    private void RefreshSecretList()
    {
        _secretNames.Clear();
        foreach (var name in _secretStore.GetSecretNames())
        {
            _secretNames.Add(name);
        }
    }

    private void RefreshProgramSearchResults(IEnumerable<ProgramSearchResult> results)
    {
        _programSearchResults.Clear();
        foreach (var result in results
                     .GroupBy(item => (item.ProgramPath ?? string.Empty) + "|" + (item.WindowTitle ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _programSearchResults.Add(result);
        }
    }

    private void RefreshLearningRecommendations()
    {
        _recommendedOrderLines.Clear();

        if (_configDocument is null)
        {
            RecommendedOrderStatusText.Text = "Load a valid config to view learned recommendations.";
            return;
        }

        var config = _configDocument.Configuration;
        var learning = config.Learning ?? new LauncherLearningOptions();
        if (!learning.Enabled)
        {
            RecommendedOrderStatusText.Text = "Learning is currently disabled in settings.";
            return;
        }

        var launchStepNames = config.Steps
            .Where(step => step.Enabled && string.Equals(step.Type, "launch", StringComparison.OrdinalIgnoreCase))
            .Select(step => step.Name)
            .ToList();

        var minRuns = Math.Max(1, learning.MinRunsBeforeSuggestions);
        var recommendations = _learningService.GetRecommendedOrder(launchStepNames, minRuns);
        if (recommendations.Count == 0)
        {
            RecommendedOrderStatusText.Text = $"No learned order yet. Run Start at least {minRuns} time(s).";
            return;
        }

        RecommendedOrderStatusText.Text = learning.AutoApplyRecommendedOrder
            ? "Auto-apply is enabled. This order will be used for launch steps."
            : "Suggested order based on your history.";

        for (var index = 0; index < recommendations.Count; index++)
        {
            _recommendedOrderLines.Add($"{index + 1}. {recommendations[index]}");
        }
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

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_runCancellationSource is null || _runCancellationSource.IsCancellationRequested)
        {
            return;
        }

        StatusText.Text = "Stopping...";
        AppendLog("Stop requested; finishing current action and halting.");
        _runCancellationSource.Cancel();
        StopButton.IsEnabled = false;
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

    private async void SearchProgramsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var query = NormalizeText(ProgramSearchQueryTextBox.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(this, "Enter a program name to search for.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            RefreshProgramSearchResults(await SearchProgramSuggestionsAsync(query));
            StatusText.Text = $"Found {_programSearchResults.Count} program match(es)";
        }
        catch (Exception ex)
        {
            AppendLog("Program search failed: " + ex.Message);
            StatusText.Text = "Program search failed";
        }
    }

    private void LearnOpenAppsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshProgramSearchResults(GetOpenApplicationSuggestions());
        StatusText.Text = $"Learned {_programSearchResults.Count} currently open app(s)";
        AppendLog($"Learned {_programSearchResults.Count} currently open app(s) into program search.");
    }

    private void StartTeachSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var captureConfirmation = MessageBox.Show(
            this,
            "Teach Session can capture window focus, mouse clicks, and keyboard input across apps while recording.\n\nDo you want to capture typed input for full replay?",
            "Launcher Native",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (captureConfirmation == MessageBoxResult.Cancel)
        {
            return;
        }

        _teachSessionCaptureSensitiveInput = captureConfirmation == MessageBoxResult.Yes;

        _teachSessionBaselineProcessIds = CaptureCurrentProcessIds();
        _teachSessionCapturedApps.Clear();
        _teachSessionCapturedKeys.Clear();
        _teachSessionPrograms.Clear();
        _teachSessionFocusEvents.Clear();
        _teachSessionInteractionEvents.Clear();
        _stepRows.Clear();
        _isTeachSessionActive = true;

        StartTeachSessionHooks();

        _teachSessionTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _teachSessionTimer.Tick -= TeachSessionTimer_OnTick;
        _teachSessionTimer.Tick += TeachSessionTimer_OnTick;
        _teachSessionTimer.Start();

        StartTeachSessionButton.IsEnabled = false;
        StopTeachSessionButton.IsEnabled = true;
        ApplyTeachSessionButton.IsEnabled = false;

        StatusText.Text = "Teach session started";
        AppendLog("Teach session started. Open apps and complete each login/workflow step now, then click Stop Teach Session.");
        MessageBox.Show(
            this,
                "Teach Session started.\n\nOpen programs and complete login/workflow steps exactly how you want them replayed.\nWhen done, click Stop Teach Session.",
            "Launcher Native",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void StopTeachSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isTeachSessionActive)
        {
            return;
        }

        _teachSessionTimer?.Stop();
        StopTeachSessionHooks();

        CaptureTeachSessionDeltas();

        _isTeachSessionActive = false;
        _teachSessionBaselineProcessIds.Clear();

        StartTeachSessionButton.IsEnabled = true;
        StopTeachSessionButton.IsEnabled = false;
        ApplyTeachSessionButton.IsEnabled = _teachSessionPrograms.Count > 0;

        if (_teachSessionPrograms.Count == 0)
        {
            StatusText.Text = "Teach session found no new apps";
            AppendLog("Teach session stopped. No newly opened apps were detected.");
            _stepRows.Clear();
            MessageBox.Show(
                this,
                "No newly opened programs were detected during this Teach Session.\n\nStart Teach Session again, then open programs after starting it.",
                "Launcher Native",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StatusText.Text = $"Teach session captured {_teachSessionPrograms.Count} app(s)";
        AppendLog($"Teach session captured {_teachSessionPrograms.Count} app(s) with {_teachSessionFocusEvents.Count} window focus transition(s). Click Apply Taught Flow to save startup steps.");
        MessageBox.Show(
            this,
            $"Captured {_teachSessionPrograms.Count} app(s).\n\nClick 'Apply Taught Flow' to save them into your startup config.",
            "Launcher Native",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ApplyTeachSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_configDocument is null)
        {
            MessageBox.Show(this, "Load a config before applying a taught flow.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_teachSessionPrograms.Count == 0)
        {
            MessageBox.Show(this, "No taught apps are waiting to be applied.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RemoveTaughtStepsFromConfig(_configDocument);
        var inventoryEntries = await _programInventoryService.GetProgramsAsync();

        var existingNames = new HashSet<string>(_configDocument.Configuration.Steps.Select(step => step.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var captured in _teachSessionPrograms.Values.OrderBy(item => item.FirstSeenUtc))
        {
            var app = captured.Program;
            if (string.IsNullOrWhiteSpace(app.ProgramPath))
            {
                continue;
            }

            var baseName = NormalizeText(app.DisplayName) ?? Path.GetFileNameWithoutExtension(app.ProgramPath);
            var stepName = BuildUniqueStepName(existingNames, "Taught - " + baseName);
            existingNames.Add(stepName);

            var processName = Path.GetFileNameWithoutExtension(app.ProgramPath);
            var observedTitles = captured.ObservedWindowTitles
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primaryWindowTitle = observedTitles.FirstOrDefault();
            var appDurationSeconds = Math.Max(1, (int)Math.Round((captured.LastSeenUtc - captured.FirstSeenUtc).TotalSeconds));
            var inventoryMatch = FindInventoryMatch(app, inventoryEntries);

            var taughtStep = new LauncherStep
            {
                Name = stepName,
                Type = "launch",
                Enabled = true,
                ProgramPath = app.ProgramPath,
                ProgramDisplayName = inventoryMatch?.DisplayName ?? app.DisplayName,
                DetectionAppId = inventoryMatch?.AppId,
                DetectionMethod = inventoryMatch?.Source,
                InventoryCachedAt = DateTimeOffset.Now.ToString("O"),
                Arguments = NormalizeText(app.Arguments),
                WorkingDirectory = NormalizeText(Path.GetDirectoryName(app.ProgramPath)),
                WindowTitle = primaryWindowTitle,
                LaunchOnlyIfMissing = true,
                PostLaunchDelaySeconds = 2,
                WaitAfterStepSeconds = appDurationSeconds,
                ProductivityNotes = TaughtStepMarker + " Captured from Teach Session on " + DateTimeOffset.Now.ToString("O") + $" | Observed windows={observedTitles.Count}",
                TaughtEvents = BuildTaughtEventsForProgram(captured),
                RunningProcessNames = string.IsNullOrWhiteSpace(processName)
                    ? new List<string>()
                    : new List<string> { processName },
                CloseProcessNames = string.IsNullOrWhiteSpace(processName)
                    ? new List<string>()
                    : new List<string> { processName },
                RunningWindowTitles = observedTitles,
                CloseWindowTitles = observedTitles
            };

            UpsertStep(taughtStep);
        }

        try
        {
            _configStore.Save(_configDocument);
            AppendLog($"Applied taught flow with {_teachSessionPrograms.Count} app(s) to config.");
            StatusText.Text = "Taught flow applied";
            _teachSessionCapturedApps.Clear();
            _teachSessionPrograms.Clear();
            _teachSessionFocusEvents.Clear();
            ApplyTeachSessionButton.IsEnabled = false;
            ReloadConfigView();
        }
        catch (Exception ex)
        {
            AppendLog("Applying taught flow failed: " + ex.Message);
            StatusText.Text = "Apply taught flow failed";
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyProgramSearchResultButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProgramSearchResultsListBox.SelectedItem is not ProgramSearchResult selected)
        {
            MessageBox.Show(this, "Select a program search result first.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ProgramStepNameTextBox.Text = selected.DisplayName;
        ProgramPathTextBox.Text = selected.ProgramPath ?? string.Empty;
        ProgramWindowTitleTextBox.Text = selected.WindowTitle ?? string.Empty;
        ProgramArgumentsTextBox.Text = selected.Arguments ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ProgramWorkingDirectoryTextBox.Text) && !string.IsNullOrWhiteSpace(selected.ProgramPath))
        {
            ProgramWorkingDirectoryTextBox.Text = Path.GetDirectoryName(selected.ProgramPath) ?? string.Empty;
        }

        StatusText.Text = $"Loaded program '{selected.DisplayName}' into builder";
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

        if (!int.TryParse(MinRunsBeforeSuggestionsTextBox.Text.Trim(), out var minRunsBeforeSuggestions) || minRunsBeforeSuggestions < 1)
        {
            MessageBox.Show(this, "Learning minimum runs must be a positive integer.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            DefaultCloseForce = DefaultCloseForceCheckBox.IsChecked == true,
            LearningEnabled = LearningEnabledCheckBox.IsChecked == true,
            ShowRecommendedOrder = ShowRecommendedOrderCheckBox.IsChecked == true,
            AutoApplyRecommendedOrder = AutoApplyRecommendedOrderCheckBox.IsChecked == true,
            MinRunsBeforeSuggestions = minRunsBeforeSuggestions
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

    private void ResetLearningHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Reset learned launch history? This cannot be undone.",
            "Launcher Native",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        if (!_learningService.ResetHistory())
        {
            MessageBox.Show(this, "Could not reset learning history file.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_configDocument is not null)
        {
            RemoveTaughtStepsFromConfig(_configDocument);
            _configStore.Save(_configDocument);
            _stepRows.Clear();
            _teachSessionCapturedApps.Clear();
            _teachSessionCapturedKeys.Clear();
            ApplyTeachSessionButton.IsEnabled = false;
            AppendLog("Cleared taught run steps after learning reset.");
        }

        AppendLog("Learning history reset: " + _learningService.StateFilePath);
        StatusText.Text = "Learning reset; Run tab is blank and ready for new teaching";
        RefreshLearningRecommendations();
    }

    private void AddOrUpdateProgramStepButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_configDocument is null)
        {
            MessageBox.Show(this, "Load a config before adding a program step.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var stepName = NormalizeText(ProgramStepNameTextBox.Text);
        var programPath = NormalizeText(ProgramPathTextBox.Text);

        if (string.IsNullOrWhiteSpace(stepName) || string.IsNullOrWhiteSpace(programPath))
        {
            MessageBox.Show(this, "Step Name and Program Path are required.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseOptionalPositiveInt(ProgramPostLaunchDelayTextBox.Text, out var postLaunchDelaySeconds) ||
            !TryParseOptionalPositiveInt(ProgramWaitAfterStepTextBox.Text, out var waitAfterStepSeconds) ||
            !TryParseOptionalPositiveInt(ProgramWaitForLoginCompleteTextBox.Text, out var waitForLoginCompleteSeconds))
        {
            MessageBox.Show(this, "Timing values must be blank or positive integers.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var windowTitle = NormalizeText(ProgramWindowTitleTextBox.Text);
        var processName = Path.GetFileNameWithoutExtension(programPath);
        var loginSequence = ParseLoginSequence(ProgramLoginKeysTextBox.Text);
        var step = new LauncherStep
        {
            Name = stepName,
            Type = "launch",
            Enabled = true,
            ProgramPath = programPath,
            Arguments = NormalizeText(ProgramArgumentsTextBox.Text),
            WorkingDirectory = NormalizeText(ProgramWorkingDirectoryTextBox.Text),
            WindowTitle = windowTitle,
            LoginWindowTitle = NormalizeText(ProgramLoginWindowTitleTextBox.Text),
            LoginSequence = loginSequence,
            WaitForLoginCompleteSeconds = waitForLoginCompleteSeconds,
            PostLaunchDelaySeconds = postLaunchDelaySeconds,
            WaitAfterStepSeconds = waitAfterStepSeconds,
            LaunchOnlyIfMissing = true,
            ProductivityNotes = NormalizeText(ProgramProductivityNotesTextBox.Text),
            RunningWindowTitles = string.IsNullOrWhiteSpace(windowTitle)
                ? new List<string>()
                : new List<string> { windowTitle },
            CloseWindowTitles = string.IsNullOrWhiteSpace(windowTitle)
                ? new List<string>()
                : new List<string> { windowTitle },
            RunningProcessNames = string.IsNullOrWhiteSpace(processName)
                ? new List<string>()
                : new List<string> { processName },
            CloseProcessNames = string.IsNullOrWhiteSpace(processName)
                ? new List<string>()
                : new List<string> { processName }
        };

        UpsertStep(step);

        try
        {
            _configStore.Save(_configDocument);
            AppendLog($"Program builder saved step '{step.Name}'.");
            StatusText.Text = $"Program step '{step.Name}' saved";
            ReloadConfigView();
        }
        catch (Exception ex)
        {
            AppendLog("Program step save failed: " + ex.Message);
            StatusText.Text = "Program step save failed";
            MessageBox.Show(this, ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateBuilderFromSelectedStepButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedStep(out var step) || step is null)
        {
            return;
        }

        ProgramStepNameTextBox.Text = step.Name;
        ProgramPathTextBox.Text = step.ProgramPath ?? string.Empty;
        ProgramArgumentsTextBox.Text = step.Arguments ?? string.Empty;
        ProgramWorkingDirectoryTextBox.Text = step.WorkingDirectory ?? string.Empty;
        ProgramWindowTitleTextBox.Text = step.WindowTitle ?? string.Empty;
        ProgramLoginWindowTitleTextBox.Text = step.LoginWindowTitle ?? string.Empty;
        ProgramPostLaunchDelayTextBox.Text = step.PostLaunchDelaySeconds?.ToString() ?? string.Empty;
        ProgramWaitAfterStepTextBox.Text = step.WaitAfterStepSeconds?.ToString() ?? string.Empty;
        ProgramWaitForLoginCompleteTextBox.Text = step.WaitForLoginCompleteSeconds?.ToString() ?? string.Empty;
        ProgramProductivityNotesTextBox.Text = step.ProductivityNotes ?? string.Empty;
        ProgramLoginKeysTextBox.Text = string.Join(
            Environment.NewLine,
            step.LoginSequence.Select(entry => entry.DelayMs is > 0
                ? $"{entry.Keys}|{entry.DelayMs}"
                : entry.Keys));

        StatusText.Text = $"Loaded '{step.Name}' into Program Builder";
    }

    private void ClearProgramBuilderButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProgramStepNameTextBox.Text = string.Empty;
        ProgramPathTextBox.Text = string.Empty;
        ProgramArgumentsTextBox.Text = string.Empty;
        ProgramWorkingDirectoryTextBox.Text = string.Empty;
        ProgramWindowTitleTextBox.Text = string.Empty;
        ProgramLoginWindowTitleTextBox.Text = string.Empty;
        ProgramPostLaunchDelayTextBox.Text = string.Empty;
        ProgramWaitAfterStepTextBox.Text = string.Empty;
        ProgramWaitForLoginCompleteTextBox.Text = string.Empty;
        ProgramLoginKeysTextBox.Text = string.Empty;
        ProgramProductivityNotesTextBox.Text = string.Empty;
        ProgramSecretNameTextBox.Text = string.Empty;
        ProgramSecretValuePasswordBox.Password = string.Empty;
        StatusText.Text = "Program Builder form cleared";
    }

    private void SaveProgramSecretButton_OnClick(object sender, RoutedEventArgs e)
    {
        var secretName = NormalizeText(ProgramSecretNameTextBox.Text);
        var secretValue = ProgramSecretValuePasswordBox.Password;
        if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretValue))
        {
            MessageBox.Show(this, "Secret Name and Secret Value are required.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_secretStore.SaveSecret(secretName, secretValue))
        {
            MessageBox.Show(this, "Could not save the secret.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProgramSecretValuePasswordBox.Password = string.Empty;
        AppendLog($"Saved secret '{secretName}' to encrypted local store.");
        StatusText.Text = $"Secret '{secretName}' saved";
        RefreshSecretList();
    }

    private void InsertSecretTokenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var secretName = NormalizeText(ProgramSecretNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(secretName))
        {
            MessageBox.Show(this, "Enter a Secret Name first.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var token = LauncherSecretStoreService.BuildToken(secretName);
        if (!string.IsNullOrWhiteSpace(ProgramLoginKeysTextBox.Text) && !ProgramLoginKeysTextBox.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            ProgramLoginKeysTextBox.Text += Environment.NewLine;
        }

        ProgramLoginKeysTextBox.Text += token;
        ProgramLoginKeysTextBox.Focus();
        ProgramLoginKeysTextBox.CaretIndex = ProgramLoginKeysTextBox.Text.Length;
        StatusText.Text = $"Inserted token for secret '{secretName}'";
    }

    private async Task<IEnumerable<ProgramSearchResult>> SearchProgramSuggestionsAsync(string query)
    {
        var results = new List<ProgramSearchResult>();
        results.AddRange(GetOpenApplicationSuggestions(query));
        results.AddRange(await GetInventorySuggestionsAsync(query));
        results.AddRange(GetStartMenuSuggestions(query));
        results.AddRange(GetPathExecutableSuggestions(query));
        return results;
    }

    private async Task<IEnumerable<ProgramSearchResult>> GetInventorySuggestionsAsync(string query)
    {
        var normalizedQuery = NormalizeText(query) ?? string.Empty;
        var entries = await _programInventoryService.GetProgramsAsync();
        return entries
            .Where(item => item.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                           (item.ProgramPath?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(item => new ProgramSearchResult
            {
                DisplayName = item.DisplayName,
                ProgramPath = item.ProgramPath,
                Source = "Inventory:" + item.Source
            })
            .ToList();
    }

    private IEnumerable<ProgramSearchResult> GetOpenApplicationSuggestions(string? query = null)
    {
        var normalizedQuery = NormalizeText(query);
        foreach (var process in Process.GetProcesses())
        {
            string processName;
            string windowTitle;
            string? path;
            try
            {
                processName = process.ProcessName;
                windowTitle = process.MainWindowTitle;
                path = process.MainModule?.FileName;
            }
            catch
            {
                continue;
            }

            if (string.Equals(processName, "Launcher", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(windowTitle) && string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery) &&
                !processName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) &&
                !windowTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) &&
                !(path?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            yield return new ProgramSearchResult
            {
                DisplayName = string.IsNullOrWhiteSpace(windowTitle) ? processName : windowTitle,
                ProgramPath = path,
                WindowTitle = windowTitle,
                Source = "Open App"
            };
        }
    }

    private static HashSet<int> CaptureCurrentProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                ids.Add(process.Id);
            }
            catch
            {
                // Ignore processes that become inaccessible during capture.
            }
        }

        return ids;
    }

    private static IEnumerable<ProgramSearchResult> CaptureNewlyOpenedPrograms(IReadOnlySet<int> baselineProcessIds)
    {
        foreach (var process in Process.GetProcesses())
        {
            string processName;
            string windowTitle;
            string? path;

            try
            {
                if (baselineProcessIds.Contains(process.Id))
                {
                    continue;
                }

                processName = process.ProcessName;
                windowTitle = process.MainWindowTitle;
                path = process.MainModule?.FileName;
            }
            catch
            {
                continue;
            }

            if (string.Equals(processName, "Launcher", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "Launcher.App", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new ProgramSearchResult
            {
                DisplayName = string.IsNullOrWhiteSpace(windowTitle) ? processName : windowTitle,
                ProgramPath = path,
                WindowTitle = windowTitle,
                Source = "Teach Session"
            };
        }
    }

    private void TeachSessionTimer_OnTick(object? sender, EventArgs e)
    {
        CaptureTeachSessionDeltas();
    }

    private void CaptureTeachSessionDeltas()
    {
        CaptureForegroundWindowEvent();

        foreach (var app in CaptureNewlyOpenedPrograms(_teachSessionBaselineProcessIds))
        {
            if (string.IsNullOrWhiteSpace(app.ProgramPath))
            {
                continue;
            }

            var programKey = app.ProgramPath;
            if (!_teachSessionPrograms.TryGetValue(programKey, out var captured))
            {
                captured = new TeachCapturedProgram(app);
                _teachSessionPrograms[programKey] = captured;
                _teachSessionCapturedApps.Add(app);
                AddTaughtStepRowPreview(app, _teachSessionCapturedApps.Count);
                AppendLog($"Taught app learned: {app.DisplayName}");
            }

            captured.ObserveWindowTitle(app.WindowTitle);

            var key = (app.ProgramPath ?? string.Empty) + "|" + (app.WindowTitle ?? string.Empty);
            if (_teachSessionCapturedKeys.Add(key) && !string.IsNullOrWhiteSpace(app.WindowTitle))
            {
                AppendLog($"Taught window observed: {app.WindowTitle}");
            }

            StatusText.Text = $"Teach session learned {_teachSessionPrograms.Count} app(s)";
        }
    }

    private static LauncherProgramInventoryEntry? FindInventoryMatch(
        ProgramSearchResult app,
        IReadOnlyList<LauncherProgramInventoryEntry> inventory)
    {
        var path = NormalizeText(app.ProgramPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var pathMatch = inventory.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.ProgramPath) &&
                string.Equals(item.ProgramPath, path, StringComparison.OrdinalIgnoreCase));

            if (pathMatch is not null)
            {
                return pathMatch;
            }
        }

        var displayName = NormalizeText(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return inventory.FirstOrDefault(item =>
                item.DisplayName.Contains(displayName, StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains(item.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private List<LauncherTaughtEvent> BuildTaughtEventsForProgram(TeachCapturedProgram captured)
    {
        var events = new List<LauncherTaughtEvent>();

        var relevantFocusEvents = _teachSessionFocusEvents
            .Where(item =>
                (!string.IsNullOrWhiteSpace(captured.Program.ProgramPath) &&
                 !string.IsNullOrWhiteSpace(item.ProcessPath) &&
                 string.Equals(item.ProcessPath, captured.Program.ProgramPath, StringComparison.OrdinalIgnoreCase)) ||
                captured.ObservedWindowTitles.Any(title => string.Equals(title, item.WindowTitle, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.ObservedAtUtc)
            .ToList();

        DateTimeOffset? previousTimestamp = null;
        foreach (var focusEvent in relevantFocusEvents)
        {
            var delay = previousTimestamp is null
                ? 0
                : Math.Max(0, (int)Math.Round((focusEvent.ObservedAtUtc - previousTimestamp.Value).TotalMilliseconds));

            events.Add(new LauncherTaughtEvent
            {
                EventType = "focus-window",
                Timestamp = focusEvent.ObservedAtUtc.ToString("O"),
                WindowTitle = focusEvent.WindowTitle,
                ProcessPath = focusEvent.ProcessPath,
                DelayMs = delay,
                Notes = "Captured during Teach Session"
            });

            previousTimestamp = focusEvent.ObservedAtUtc;
        }

        var relevantInteractionEvents = _teachSessionInteractionEvents
            .Where(item =>
                (!string.IsNullOrWhiteSpace(captured.Program.ProgramPath) &&
                 !string.IsNullOrWhiteSpace(item.ProcessPath) &&
                 string.Equals(item.ProcessPath, captured.Program.ProgramPath, StringComparison.OrdinalIgnoreCase)) ||
                captured.ObservedWindowTitles.Any(title => string.Equals(title, item.WindowTitle, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.ObservedAtUtc)
            .ToList();

        foreach (var interactionEvent in relevantInteractionEvents)
        {
            var delay = previousTimestamp is null
                ? 0
                : Math.Max(0, (int)Math.Round((interactionEvent.ObservedAtUtc - previousTimestamp.Value).TotalMilliseconds));

            events.Add(new LauncherTaughtEvent
            {
                EventType = interactionEvent.EventType,
                Timestamp = interactionEvent.ObservedAtUtc.ToString("O"),
                WindowTitle = interactionEvent.WindowTitle,
                ProcessPath = interactionEvent.ProcessPath,
                DelayMs = delay,
                InputValue = interactionEvent.InputValue,
                MouseButton = interactionEvent.MouseButton,
                MouseX = interactionEvent.MouseX,
                MouseY = interactionEvent.MouseY,
                Notes = "Captured during Teach Session"
            });

            previousTimestamp = interactionEvent.ObservedAtUtc;
        }

        return events;
    }

    private void StartTeachSessionHooks()
    {
        if (_keyboardHookHandle != IntPtr.Zero || _mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;

        var moduleHandle = GetModuleHandle(null);
        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);

        AppendLog(_teachSessionCaptureSensitiveInput
            ? "Teach hooks active: recording focus, clicks, and key input."
            : "Teach hooks active: recording focus and clicks (key input masked)." );
    }

    private void StopTeachSessionHooks()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_isTeachSessionActive)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (message != WM_KEYDOWN && message != WM_SYSKEYDOWN)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var keyInfo = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var inputValue = _teachSessionCaptureSensitiveInput
            ? BuildKeyInputValue((int)keyInfo.vkCode)
            : "[captured-key]";

        RecordTeachInteractionEvent(new TeachInteractionEvent
        {
            ObservedAtUtc = DateTimeOffset.Now,
            EventType = "key-input",
            InputValue = inputValue,
            WindowTitle = GetForegroundWindowTitle(),
            ProcessPath = GetForegroundProcessPath()
        });

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_isTeachSessionActive)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var button = message switch
        {
            WM_LBUTTONDOWN => "left",
            WM_RBUTTONDOWN => "right",
            _ => null
        };

        if (button is null)
        {
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var mouseInfo = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        RecordTeachInteractionEvent(new TeachInteractionEvent
        {
            ObservedAtUtc = DateTimeOffset.Now,
            EventType = "mouse-click",
            MouseButton = button,
            MouseX = mouseInfo.pt.x,
            MouseY = mouseInfo.pt.y,
            WindowTitle = GetForegroundWindowTitle(),
            ProcessPath = GetForegroundProcessPath()
        });

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void RecordTeachInteractionEvent(TeachInteractionEvent interactionEvent)
    {
        lock (_teachSessionEventLock)
        {
            _teachSessionInteractionEvents.Add(interactionEvent);
        }
    }

    private static string BuildKeyInputValue(int virtualKeyCode)
    {
        if (virtualKeyCode >= 'A' && virtualKeyCode <= 'Z')
        {
            return char.ToLowerInvariant((char)virtualKeyCode).ToString(CultureInfo.InvariantCulture);
        }

        if (virtualKeyCode >= '0' && virtualKeyCode <= '9')
        {
            return ((char)virtualKeyCode).ToString(CultureInfo.InvariantCulture);
        }

        return virtualKeyCode switch
        {
            0x0D => "{ENTER}",
            0x09 => "{TAB}",
            0x08 => "{BACKSPACE}",
            0x1B => "{ESC}",
            0x20 => " ",
            _ => "{" + virtualKeyCode + "}"
        };
    }

    private static string? GetForegroundProcessPath()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(handle, out var processId);
        try
        {
            return Process.GetProcessById((int)processId).MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string GetForegroundWindowTitle()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        return GetWindowTitle(handle);
    }

    private void CaptureForegroundWindowEvent()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        GetWindowThreadProcessId(handle, out var processId);
        var eventTime = DateTimeOffset.Now;

        string? processPath = null;
        try
        {
            var process = Process.GetProcessById((int)processId);
            processPath = process.MainModule?.FileName;
        }
        catch
        {
            processPath = null;
        }

        if (_teachSessionFocusEvents.Count > 0)
        {
            var last = _teachSessionFocusEvents[_teachSessionFocusEvents.Count - 1];
            if (string.Equals(last.WindowTitle, title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(last.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        _teachSessionFocusEvents.Add(new TeachFocusEvent
        {
            ObservedAtUtc = eventTime,
            WindowTitle = title,
            ProcessPath = processPath
        });
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private void AddTaughtStepRowPreview(ProgramSearchResult app, int sequenceNumber)
    {
        var displayName = NormalizeText(app.DisplayName) ?? "Taught Program " + sequenceNumber;
        _stepRows.Add(new StepRow
        {
            Name = "Taught - " + displayName,
            Type = "launch",
            Enabled = true,
            ProgramPath = app.ProgramPath ?? string.Empty
        });
    }

    private static IEnumerable<LauncherStep> GetTaughtSteps(LauncherConfiguration config)
    {
        return config.Steps.Where(IsTaughtStep);
    }

    private static bool IsTaughtStep(LauncherStep step)
    {
        return (!string.IsNullOrWhiteSpace(step.ProductivityNotes) && step.ProductivityNotes.Contains(TaughtStepMarker, StringComparison.OrdinalIgnoreCase)) ||
               step.Name.StartsWith("Taught - ", StringComparison.OrdinalIgnoreCase);
    }

    private static LauncherConfigDocument BuildRuntimeDocumentForTaughtSteps(LauncherConfigDocument source, IReadOnlyList<LauncherStep> taughtSteps)
    {
        var runtimeRoot = source.Root.DeepClone() as JsonObject ?? new JsonObject();
        runtimeRoot["steps"] = JsonSerializer.SerializeToNode(taughtSteps) as JsonArray ?? new JsonArray();

        var runtimeConfiguration = runtimeRoot.Deserialize<LauncherConfiguration>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new LauncherConfiguration();

        return new LauncherConfigDocument
        {
            FilePath = source.FilePath,
            Root = runtimeRoot,
            Configuration = runtimeConfiguration
        };
    }

    private static void RemoveTaughtStepsFromConfig(LauncherConfigDocument document)
    {
        document.Configuration.Steps = document.Configuration.Steps
            .Where(step => !IsTaughtStep(step))
            .ToList();

        if (document.Root["steps"] is not JsonArray stepsArray)
        {
            return;
        }

        for (var i = stepsArray.Count - 1; i >= 0; i--)
        {
            if (stepsArray[i] is not JsonObject stepObject)
            {
                continue;
            }

            var name = stepObject["name"]?.GetValue<string>() ?? string.Empty;
            var notes = stepObject["productivityNotes"]?.GetValue<string>() ?? string.Empty;
            if (name.StartsWith("Taught - ", StringComparison.OrdinalIgnoreCase) ||
                notes.Contains(TaughtStepMarker, StringComparison.OrdinalIgnoreCase))
            {
                stepsArray.RemoveAt(i);
            }
        }
    }

    private static string BuildUniqueStepName(IReadOnlySet<string> existingNames, string baseName)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseName) ? "Taught Program" : baseName.Trim();
        if (!existingNames.Contains(normalizedBase))
        {
            return normalizedBase;
        }

        var suffix = 2;
        while (existingNames.Contains(normalizedBase + " " + suffix))
        {
            suffix++;
        }

        return normalizedBase + " " + suffix;
    }

    private IEnumerable<ProgramSearchResult> GetStartMenuSuggestions(string query)
    {
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }
        .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory));

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic? shell = shellType is null ? null : Activator.CreateInstance(shellType);

        foreach (var directory in directories)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var displayName = Path.GetFileNameWithoutExtension(filePath);
                if (!displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var programPath = filePath;
                if (shell is not null && filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        dynamic shortcut = shell.CreateShortcut(filePath);
                        programPath = (string)shortcut.TargetPath;
                    }
                    catch
                    {
                        continue;
                    }
                }

                yield return new ProgramSearchResult
                {
                    DisplayName = displayName,
                    ProgramPath = programPath,
                    Source = "Start Menu"
                };
            }
        }
    }

    private IEnumerable<ProgramSearchResult> GetPathExecutableSuggestions(string query)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var displayName = Path.GetFileNameWithoutExtension(filePath);
                if (!displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new ProgramSearchResult
                {
                    DisplayName = displayName,
                    ProgramPath = filePath,
                    Source = "PATH"
                };
            }
        }
    }

    private void RefreshSecretsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshSecretList();
        StatusText.Text = "Secret list refreshed";
    }

    private async void RefreshInventoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Refreshing inventory...";
            var entries = await _programInventoryService.GetProgramsAsync(forceRefresh: true);
            StatusText.Text = $"Program inventory refreshed ({entries.Count} entries)";
            AppendLog($"Program inventory refreshed ({entries.Count} entries).");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Program inventory refresh failed";
            AppendLog("Program inventory refresh failed: " + ex.Message);
        }
    }

    private void RenameSecretButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SecretsListBox.SelectedItem is not string oldName || string.IsNullOrWhiteSpace(oldName))
        {
            MessageBox.Show(this, "Select a secret to rename.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newName = NormalizeText(RenameSecretNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this, "Enter the new secret name.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_secretStore.RenameSecret(oldName, newName))
        {
            MessageBox.Show(this, "Could not rename secret. Confirm the new name is unique.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AppendLog($"Renamed secret '{oldName}' to '{newName}'.");
        ProgramSecretNameTextBox.Text = newName;
        RenameSecretNameTextBox.Text = string.Empty;
        RefreshSecretList();
        SecretsListBox.SelectedItem = newName;
        StatusText.Text = "Secret renamed";
    }

    private void DeleteSecretButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SecretsListBox.SelectedItem is not string selectedName || string.IsNullOrWhiteSpace(selectedName))
        {
            MessageBox.Show(this, "Select a secret to delete.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete secret '{selectedName}'? This cannot be undone.",
            "Launcher Native",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        if (!_secretStore.DeleteSecret(selectedName))
        {
            MessageBox.Show(this, "Could not delete secret.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AppendLog($"Deleted secret '{selectedName}'.");
        if (string.Equals(ProgramSecretNameTextBox.Text?.Trim(), selectedName, StringComparison.OrdinalIgnoreCase))
        {
            ProgramSecretNameTextBox.Text = string.Empty;
        }

        RefreshSecretList();
        StatusText.Text = "Secret deleted";
    }

    private void SecretsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SecretsListBox.SelectedItem is not string selectedName || string.IsNullOrWhiteSpace(selectedName))
        {
            return;
        }

        ProgramSecretNameTextBox.Text = selectedName;
    }

    private static string? NormalizeText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryParseOptionalPositiveInt(string value, out int? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!int.TryParse(value.Trim(), out var numericValue) || numericValue < 1)
        {
            return false;
        }

        parsed = numericValue;
        return true;
    }

    private static List<LauncherKeySequenceEntry> ParseLoginSequence(string? text)
    {
        var entries = new List<LauncherKeySequenceEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return entries;
        }

        var lines = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        foreach (var line in lines)
        {
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            var keys = parts[0];
            if (string.IsNullOrWhiteSpace(keys))
            {
                continue;
            }

            int? delayMs = null;
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedDelay) && parsedDelay > 0)
            {
                delayMs = parsedDelay;
            }

            entries.Add(new LauncherKeySequenceEntry
            {
                Keys = keys,
                DelayMs = delayMs
            });
        }

        return entries;
    }

    private void UpsertStep(LauncherStep step)
    {
        if (_configDocument is null)
        {
            return;
        }

        var configSteps = _configDocument.Configuration.Steps;
        var existingConfigIndex = configSteps.FindIndex(s => string.Equals(s.Name, step.Name, StringComparison.OrdinalIgnoreCase));
        if (existingConfigIndex >= 0)
        {
            configSteps[existingConfigIndex] = step;
        }
        else
        {
            configSteps.Add(step);
        }

        if (_configDocument.Root["steps"] is not JsonArray stepsArray)
        {
            stepsArray = new JsonArray();
            _configDocument.Root["steps"] = stepsArray;
        }

        var stepNode = JsonSerializer.SerializeToNode(step) as JsonObject ?? new JsonObject();
        var existingRootIndex = -1;
        for (var i = 0; i < stepsArray.Count; i++)
        {
            if (stepsArray[i] is not JsonObject existingObject)
            {
                continue;
            }

            var existingName = existingObject["name"]?.GetValue<string>();
            if (string.Equals(existingName, step.Name, StringComparison.OrdinalIgnoreCase))
            {
                existingRootIndex = i;
                break;
            }
        }

        if (existingRootIndex >= 0)
        {
            stepsArray[existingRootIndex] = stepNode;
        }
        else
        {
            stepsArray.Add(stepNode);
        }
    }

    private sealed class StepRow
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string ProgramPath { get; set; } = string.Empty;
    }

    private sealed class ProgramSearchResult
    {
        public string DisplayName { get; set; } = string.Empty;

        public string? ProgramPath { get; set; }

        public string? WindowTitle { get; set; }

        public string? Arguments { get; set; }

        public string Source { get; set; } = string.Empty;

        public string DisplayText => string.IsNullOrWhiteSpace(ProgramPath)
            ? $"{DisplayName} [{Source}]"
            : $"{DisplayName} [{Source}] - {ProgramPath}";
    }

    private sealed class TeachCapturedProgram
    {
        public TeachCapturedProgram(ProgramSearchResult program)
        {
            Program = program;
            FirstSeenUtc = DateTimeOffset.Now;
            LastSeenUtc = DateTimeOffset.Now;
            ObserveWindowTitle(program.WindowTitle);
        }

        public ProgramSearchResult Program { get; }

        public DateTimeOffset FirstSeenUtc { get; }

        public DateTimeOffset LastSeenUtc { get; private set; }

        public List<string> ObservedWindowTitles { get; } = new();

        public void ObserveWindowTitle(string? title)
        {
            LastSeenUtc = DateTimeOffset.Now;
            var normalized = NormalizeText(title);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!ObservedWindowTitles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                ObservedWindowTitles.Add(normalized);
            }
        }
    }

    private sealed class TeachFocusEvent
    {
        public DateTimeOffset ObservedAtUtc { get; set; }

        public string WindowTitle { get; set; } = string.Empty;

        public string? ProcessPath { get; set; }
    }

    private sealed class TeachInteractionEvent
    {
        public DateTimeOffset ObservedAtUtc { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string? InputValue { get; set; }

        public string? MouseButton { get; set; }

        public int? MouseX { get; set; }

        public int? MouseY { get; set; }

        public string? WindowTitle { get; set; }

        public string? ProcessPath { get; set; }
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hmod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}