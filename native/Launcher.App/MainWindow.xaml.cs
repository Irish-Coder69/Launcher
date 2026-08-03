using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    private readonly LauncherConfigStore _configStore = new();
    private readonly LauncherScriptBridge _scriptBridge = new();
    private readonly LauncherNativeDetectionService _nativeDetectionService = new();
    private readonly LauncherNativeStartRunner _nativeStartRunner = new();
    private readonly LauncherLearningService _learningService = new();
    private readonly LauncherSecretStoreService _secretStore = new();
    private readonly ObservableCollection<string> _logLines = new();
    private readonly ObservableCollection<StepRow> _stepRows = new();
    private readonly ObservableCollection<string> _recommendedOrderLines = new();
    private readonly ObservableCollection<string> _secretNames = new();
    private readonly ObservableCollection<ProgramSearchResult> _programSearchResults = new();
    private bool _isBusy;
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
        LogListBox.ItemsSource = _logLines;
        StepsGrid.ItemsSource = _stepRows;
        RecommendedOrderListBox.ItemsSource = _recommendedOrderLines;
        SecretsListBox.ItemsSource = _secretNames;
        ProgramSearchResultsListBox.ItemsSource = _programSearchResults;

        DetectLauncherPaths();
        ReloadConfigView();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        CurrentUserTextBlock.Text = _currentUser is null
            ? "Signed in user: none"
            : $"Signed in user: {_currentUser.DisplayName} ({_currentUser.UserName})";
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
            RefreshLearningRecommendations();
            RefreshSecretList();
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

                await _nativeStartRunner.RunAsync(
                    _configDocument,
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
        LearnOpenAppsButton.IsEnabled = !isBusy;
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

    private void SearchProgramsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var query = NormalizeText(ProgramSearchQueryTextBox.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(this, "Enter a program name to search for.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RefreshProgramSearchResults(SearchProgramSuggestions(query));
        StatusText.Text = $"Found {_programSearchResults.Count} program match(es)";
    }

    private void LearnOpenAppsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshProgramSearchResults(GetOpenApplicationSuggestions());
        StatusText.Text = $"Learned {_programSearchResults.Count} currently open app(s)";
        AppendLog($"Learned {_programSearchResults.Count} currently open app(s) into program search.");
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

        AppendLog("Learning history reset: " + _learningService.StateFilePath);
        StatusText.Text = "Learning history reset";
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

    private IEnumerable<ProgramSearchResult> SearchProgramSuggestions(string query)
    {
        var results = new List<ProgramSearchResult>();
        results.AddRange(GetOpenApplicationSuggestions(query));
        results.AddRange(GetStartMenuSuggestions(query));
        results.AddRange(GetPathExecutableSuggestions(query));
        return results;
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
}