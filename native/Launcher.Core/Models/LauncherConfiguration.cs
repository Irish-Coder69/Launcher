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

    [JsonPropertyName("runningWindowTitles")]
    public List<string> RunningWindowTitles { get; set; } = new();

    [JsonPropertyName("runningProcessNames")]
    public List<string> RunningProcessNames { get; set; } = new();

    [JsonPropertyName("windowTitle")]
    public string? WindowTitle { get; set; }

    [JsonPropertyName("fallbackWindowTitles")]
    public List<string> FallbackWindowTitles { get; set; } = new();

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
