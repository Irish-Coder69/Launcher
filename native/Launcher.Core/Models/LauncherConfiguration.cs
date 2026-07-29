using System.Text.Json.Serialization;

namespace Launcher.Core.Models;

public sealed class LauncherConfiguration
{
    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; }

    [JsonPropertyName("ensureCapsLockOn")]
    public bool EnsureCapsLockOn { get; set; } = true;

    [JsonPropertyName("ensureNumLockOn")]
    public bool EnsureNumLockOn { get; set; } = true;

    [JsonPropertyName("closeOptions")]
    public LauncherCloseOptions CloseOptions { get; set; } = new();

    [JsonPropertyName("steps")]
    public List<LauncherStep> Steps { get; set; } = new();
}

public sealed class LauncherCloseOptions
{
    [JsonPropertyName("closeOnlyTrackedApps")]
    public bool CloseOnlyTrackedApps { get; set; } = true;

    [JsonPropertyName("defaultCloseMethod")]
    public string DefaultCloseMethod { get; set; } = "both";

    [JsonPropertyName("defaultCloseTimeoutSeconds")]
    public int DefaultCloseTimeoutSeconds { get; set; } = 12;

    [JsonPropertyName("defaultCloseForce")]
    public bool DefaultCloseForce { get; set; }
}

public sealed class LauncherStep
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("programPath")]
    public string? ProgramPath { get; set; }

    [JsonPropertyName("fallbackProgramPath")]
    public string? FallbackProgramPath { get; set; }

    [JsonPropertyName("databasePath")]
    public string? DatabasePath { get; set; }

    [JsonPropertyName("sql")]
    public List<string> Sql { get; set; } = new();

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("windowStyle")]
    public string? WindowStyle { get; set; }

    [JsonPropertyName("postLaunchDelaySeconds")]
    public int? PostLaunchDelaySeconds { get; set; }

    [JsonPropertyName("waitAfterStepSeconds")]
    public int? WaitAfterStepSeconds { get; set; }

    [JsonPropertyName("launchOnlyIfMissing")]
    public bool? LaunchOnlyIfMissing { get; set; }

    [JsonPropertyName("runningWindowTitles")]
    public List<string> RunningWindowTitles { get; set; } = new();

    [JsonPropertyName("runningProcessNames")]
    public List<string> RunningProcessNames { get; set; } = new();

    [JsonPropertyName("windowTitle")]
    public string? WindowTitle { get; set; }

    [JsonPropertyName("fallbackWindowTitles")]
    public List<string> FallbackWindowTitles { get; set; } = new();

    [JsonPropertyName("waitForWindowToCloseTitle")]
    public string? WaitForWindowToCloseTitle { get; set; }

    [JsonPropertyName("waitForWindowToCloseTimeoutSeconds")]
    public int? WaitForWindowToCloseTimeoutSeconds { get; set; }

    [JsonPropertyName("waitForWindowToCloseDetectionTimeoutSeconds")]
    public int? WaitForWindowToCloseDetectionTimeoutSeconds { get; set; }

    [JsonPropertyName("windowTimeoutSeconds")]
    public int? WindowTimeoutSeconds { get; set; }

    [JsonPropertyName("loginWindowTitle")]
    public string? LoginWindowTitle { get; set; }

    [JsonPropertyName("loginFallbackWindowTitles")]
    public List<string> LoginFallbackWindowTitles { get; set; } = new();

    [JsonPropertyName("loginSequence")]
    public List<LauncherKeySequenceEntry> LoginSequence { get; set; } = new();

    [JsonPropertyName("waitForLoginCompleteSeconds")]
    public int? WaitForLoginCompleteSeconds { get; set; }

    [JsonPropertyName("loginSuccessValidationEnabled")]
    public bool? LoginSuccessValidationEnabled { get; set; }

    [JsonPropertyName("loginSuccessWindowTitle")]
    public string? LoginSuccessWindowTitle { get; set; }

    [JsonPropertyName("loginSuccessFallbackWindowTitles")]
    public List<string> LoginSuccessFallbackWindowTitles { get; set; } = new();

    [JsonPropertyName("loginSuccessTimeoutSeconds")]
    public int? LoginSuccessTimeoutSeconds { get; set; }

    [JsonPropertyName("loginSuccessIntervalMs")]
    public int? LoginSuccessIntervalMs { get; set; }

    [JsonPropertyName("loginSuccessRequireLoginWindowClosed")]
    public bool? LoginSuccessRequireLoginWindowClosed { get; set; }

    [JsonPropertyName("loginSuccessRequireMainWindowVisible")]
    public bool? LoginSuccessRequireMainWindowVisible { get; set; }

    [JsonPropertyName("loginSuccessControlNames")]
    public List<string> LoginSuccessControlNames { get; set; } = new();

    [JsonPropertyName("loginSuccessRequireControlMatch")]
    public bool? LoginSuccessRequireControlMatch { get; set; }

    [JsonPropertyName("moveToMonitorBeforeLogin")]
    public bool? MoveToMonitorBeforeLogin { get; set; }

    [JsonPropertyName("moveToMonitorAfterLogin")]
    public bool? MoveToMonitorAfterLogin { get; set; }

    [JsonPropertyName("targetMonitor")]
    public string? TargetMonitor { get; set; }

    [JsonPropertyName("moveWindowSearchTimeoutSeconds")]
    public int? MoveWindowSearchTimeoutSeconds { get; set; }

    [JsonPropertyName("moveWindowSearchIntervalMs")]
    public int? MoveWindowSearchIntervalMs { get; set; }

    [JsonPropertyName("additionalWindowsToMoveAfterLogin")]
    public List<LauncherWindowSpec> AdditionalWindowsToMoveAfterLogin { get; set; } = new();

    [JsonPropertyName("maximizeBeforeLogin")]
    public bool? MaximizeBeforeLogin { get; set; }

    [JsonPropertyName("maximizeWindowTitles")]
    public List<string> MaximizeWindowTitles { get; set; } = new();

    [JsonPropertyName("preLoginMinimizeWindowTitles")]
    public List<string> PreLoginMinimizeWindowTitles { get; set; } = new();

    [JsonPropertyName("preLoginMinimizeDelaySeconds")]
    public int? PreLoginMinimizeDelaySeconds { get; set; }

    [JsonPropertyName("minimizeAfterLaunch")]
    public bool? MinimizeAfterLaunch { get; set; }

    [JsonPropertyName("minimizeAfterLaunchDelaySeconds")]
    public int? MinimizeAfterLaunchDelaySeconds { get; set; }

    [JsonPropertyName("minimizeAfterCompletion")]
    public bool? MinimizeAfterCompletion { get; set; }

    [JsonPropertyName("minimizeAfterCompletionDelaySeconds")]
    public int? MinimizeAfterCompletionDelaySeconds { get; set; }

    [JsonPropertyName("minimizeAdditionalWindowTitlesAfterCompletionDelaySeconds")]
    public int? MinimizeAdditionalWindowTitlesAfterCompletionDelaySeconds { get; set; }

    [JsonPropertyName("minimizeAdditionalWindowTitlesAfterCompletion")]
    public List<string> MinimizeAdditionalWindowTitlesAfterCompletion { get; set; } = new();

    [JsonPropertyName("minimizeProcessNames")]
    public List<string> MinimizeProcessNames { get; set; } = new();

    [JsonPropertyName("minimizeWindowTitles")]
    public List<string> MinimizeWindowTitles { get; set; } = new();

    [JsonPropertyName("minimizeWindowTimeoutSeconds")]
    public int? MinimizeWindowTimeoutSeconds { get; set; }

    [JsonPropertyName("maximizeBeforeMinimizeDelayMs")]
    public int? MaximizeBeforeMinimizeDelayMs { get; set; }

    [JsonPropertyName("updateTableFlow")]
    public LauncherUpdateTableFlow? UpdateTableFlow { get; set; }

    [JsonPropertyName("closeEnabled")]
    public bool CloseEnabled { get; set; } = true;

    [JsonPropertyName("closeMethod")]
    public string CloseMethod { get; set; } = "both";

    [JsonPropertyName("closeTimeoutSeconds")]
    public int CloseTimeoutSeconds { get; set; } = 12;

    [JsonPropertyName("closeForce")]
    public bool CloseForce { get; set; }

    [JsonPropertyName("closeWindowTitles")]
    public List<string> CloseWindowTitles { get; set; } = new();

    [JsonPropertyName("closeProcessNames")]
    public List<string> CloseProcessNames { get; set; } = new();
}

public sealed class LauncherKeySequenceEntry
{
    [JsonPropertyName("keys")]
    public string Keys { get; set; } = string.Empty;

    [JsonPropertyName("delayMs")]
    public int? DelayMs { get; set; }
}

public sealed class LauncherWindowSpec
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("windowTitle")]
    public string? WindowTitle { get; set; }

    [JsonPropertyName("fallbackWindowTitles")]
    public List<string> FallbackWindowTitles { get; set; } = new();
}

public sealed class LauncherUpdateTableFlow
{
    [JsonPropertyName("mainWindowTitle")]
    public string? MainWindowTitle { get; set; }

    [JsonPropertyName("actionDelayMs")]
    public int? ActionDelayMs { get; set; }

    [JsonPropertyName("postLoginWaitSeconds")]
    public int? PostLoginWaitSeconds { get; set; }

    [JsonPropertyName("updateTableButtonName")]
    public string? UpdateTableButtonName { get; set; }

    [JsonPropertyName("updateTableButtonNames")]
    public List<string> UpdateTableButtonNames { get; set; } = new();

    [JsonPropertyName("updateTableButtonTimeoutSeconds")]
    public int? UpdateTableButtonTimeoutSeconds { get; set; }

    [JsonPropertyName("updateTableFallbackKeys")]
    public List<string> UpdateTableFallbackKeys { get; set; } = new();

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("passwordWindowTitle")]
    public string? PasswordWindowTitle { get; set; }

    [JsonPropertyName("passwordWindowTitles")]
    public List<string> PasswordWindowTitles { get; set; } = new();

    [JsonPropertyName("passwordWindowFallbackTitles")]
    public List<string> PasswordWindowFallbackTitles { get; set; } = new();

    [JsonPropertyName("passwordWindowTimeoutSeconds")]
    public int? PasswordWindowTimeoutSeconds { get; set; }

    [JsonPropertyName("passwordEnterKeys")]
    public string? PasswordEnterKeys { get; set; }

    [JsonPropertyName("waitAfterPasswordSeconds")]
    public int? WaitAfterPasswordSeconds { get; set; }
}
