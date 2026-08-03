using System.Text.Json.Serialization;

namespace Launcher.Core.Models;

public sealed class LauncherUserProfile
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("passwordSecretName")]
    public string PasswordSecretName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("loginCount")]
    public int LoginCount { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public string LastLoginAt { get; set; } = string.Empty;

    [JsonPropertyName("loginHistory")]
    public List<LauncherUserLoginEntry> LoginHistory { get; set; } = new();

    [JsonPropertyName("issueHistory")]
    public List<LauncherUserIssueEntry> IssueHistory { get; set; } = new();
}

public sealed class LauncherUserLoginEntry
{
    [JsonPropertyName("loggedInAt")]
    public string LoggedInAt { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("issueNote")]
    public string IssueNote { get; set; } = string.Empty;
}

public sealed class LauncherUserIssueEntry
{
    [JsonPropertyName("recordedAt")]
    public string RecordedAt { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

public sealed class LauncherCreateUserInput
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class LauncherUpdateUserInput
{
    public string OriginalUserName { get; set; } = string.Empty;

    public string NewUserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}
