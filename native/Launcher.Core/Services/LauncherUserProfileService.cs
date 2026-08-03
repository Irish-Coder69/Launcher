using System.IO;
using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherUserProfileService
{
    private readonly string _storePath;
    private readonly LauncherSecretStoreService _secretStore;

    public LauncherUserProfileService(string? storePath = null, LauncherSecretStoreService? secretStore = null)
    {
        _storePath = string.IsNullOrWhiteSpace(storePath)
            ? Path.Combine(GetLauncherDataDirectory(), "launcher-users.json")
            : storePath;
        _secretStore = secretStore ?? new LauncherSecretStoreService();
    }

    public IReadOnlyList<LauncherUserProfile> GetUsers()
    {
        return LoadState().Users
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool CreateUser(LauncherCreateUserInput input, out string errorMessage)
    {
        errorMessage = string.Empty;
        var userName = Normalize(input.UserName);
        var displayName = Normalize(input.DisplayName);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(displayName))
        {
            errorMessage = "User name and display name are required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.Password))
        {
            errorMessage = "Password is required.";
            return false;
        }

        var state = LoadState();
        if (state.Users.Any(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "That user name already exists.";
            return false;
        }

        var secretName = BuildPasswordSecretName(userName);
        if (!_secretStore.SaveSecret(secretName, input.Password))
        {
            errorMessage = "Could not save the user password securely.";
            return false;
        }

        state.Users.Add(new LauncherUserProfile
        {
            UserName = userName,
            DisplayName = displayName,
            Email = Normalize(input.Email) ?? string.Empty,
            Department = Normalize(input.Department) ?? string.Empty,
            Notes = Normalize(input.Notes) ?? string.Empty,
            PasswordSecretName = secretName,
            CreatedAt = DateTimeOffset.Now.ToString("O")
        });

        SaveState(state);
        return true;
    }

    public bool TryAuthenticate(string userName, string password, out LauncherUserProfile? user, out string errorMessage)
    {
        user = null;
        errorMessage = string.Empty;
        var normalizedUserName = Normalize(userName);
        if (string.IsNullOrWhiteSpace(normalizedUserName) || string.IsNullOrWhiteSpace(password))
        {
            errorMessage = "User name and password are required.";
            return false;
        }

        var state = LoadState();
        user = state.Users.FirstOrDefault(entry => string.Equals(entry.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            errorMessage = "User not found.";
            return false;
        }

        if (!_secretStore.TryGetSecret(user.PasswordSecretName, out var savedPassword))
        {
            errorMessage = "Stored user password could not be read.";
            return false;
        }

        if (!string.Equals(savedPassword, password, StringComparison.Ordinal))
        {
            errorMessage = "Incorrect password.";
            user = null;
            return false;
        }

        return true;
    }

    public bool UpdateUser(LauncherUpdateUserInput input, out string errorMessage)
    {
        errorMessage = string.Empty;
        var originalUserName = Normalize(input.OriginalUserName);
        var newUserName = Normalize(input.NewUserName);
        var displayName = Normalize(input.DisplayName);

        if (string.IsNullOrWhiteSpace(originalUserName) || string.IsNullOrWhiteSpace(newUserName) || string.IsNullOrWhiteSpace(displayName))
        {
            errorMessage = "User name and display name are required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.CurrentPassword))
        {
            errorMessage = "Current password is required to edit this user.";
            return false;
        }

        var state = LoadState();
        var user = state.Users.FirstOrDefault(entry => string.Equals(entry.UserName, originalUserName, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            errorMessage = "User not found.";
            return false;
        }

        if (!_secretStore.TryGetSecret(user.PasswordSecretName, out var savedPassword) || !string.Equals(savedPassword, input.CurrentPassword, StringComparison.Ordinal))
        {
            errorMessage = "Current password is incorrect.";
            return false;
        }

        var isUserNameChanged = !string.Equals(originalUserName, newUserName, StringComparison.OrdinalIgnoreCase);
        if (isUserNameChanged && state.Users.Any(entry => !ReferenceEquals(entry, user) && string.Equals(entry.UserName, newUserName, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "That user name already exists.";
            return false;
        }

        var existingSecretName = user.PasswordSecretName;
        var desiredSecretName = BuildPasswordSecretName(newUserName);
        var newPassword = Normalize(input.NewPassword);

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (!_secretStore.SaveSecret(desiredSecretName, newPassword))
            {
                errorMessage = "Could not securely save the new password.";
                return false;
            }

            if (!string.Equals(existingSecretName, desiredSecretName, StringComparison.OrdinalIgnoreCase))
            {
                _secretStore.DeleteSecret(existingSecretName);
            }

            user.PasswordSecretName = desiredSecretName;
        }
        else if (!string.Equals(existingSecretName, desiredSecretName, StringComparison.OrdinalIgnoreCase))
        {
            if (!_secretStore.RenameSecret(existingSecretName, desiredSecretName))
            {
                errorMessage = "Could not move the stored password to the updated user name.";
                return false;
            }

            user.PasswordSecretName = desiredSecretName;
        }

        user.UserName = newUserName;
        user.DisplayName = displayName;
        user.Email = Normalize(input.Email) ?? string.Empty;
        user.Department = Normalize(input.Department) ?? string.Empty;
        user.Notes = Normalize(input.Notes) ?? string.Empty;

        SaveState(state);
        return true;
    }

    public void RecordLogin(string userName, string? issueNote)
    {
        var normalizedUserName = Normalize(userName);
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return;
        }

        var state = LoadState();
        var user = state.Users.FirstOrDefault(entry => string.Equals(entry.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("O");
        user.LoginCount++;
        user.LastLoginAt = timestamp;
        user.LoginHistory.Add(new LauncherUserLoginEntry
        {
            LoggedInAt = timestamp,
            MachineName = Environment.MachineName,
            IssueNote = Normalize(issueNote) ?? string.Empty
        });

        if (!string.IsNullOrWhiteSpace(issueNote))
        {
            user.IssueHistory.Add(new LauncherUserIssueEntry
            {
                RecordedAt = timestamp,
                Summary = issueNote.Trim()
            });
        }

        user.LoginHistory = user.LoginHistory
            .OrderByDescending(entry => entry.LoggedInAt)
            .Take(25)
            .ToList();
        user.IssueHistory = user.IssueHistory
            .OrderByDescending(entry => entry.RecordedAt)
            .Take(50)
            .ToList();

        SaveState(state);
    }

    private static string BuildPasswordSecretName(string userName)
    {
        return $"user-login:{userName.Trim().ToLowerInvariant()}";
    }

    private static string GetLauncherDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private LauncherUserState LoadState()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new LauncherUserState();
            }

            var json = File.ReadAllText(_storePath);
            var state = JsonSerializer.Deserialize<LauncherUserState>(json);
            return state ?? new LauncherUserState();
        }
        catch
        {
            return new LauncherUserState();
        }
    }

    private void SaveState(LauncherUserState state)
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class LauncherUserState
    {
        public List<LauncherUserProfile> Users { get; set; } = new();
    }
}
