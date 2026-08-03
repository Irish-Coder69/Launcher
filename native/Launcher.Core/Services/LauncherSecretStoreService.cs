using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Launcher.Core.Services;

public sealed class LauncherSecretStoreService
{
    private static readonly Regex SecretTokenRegex = new("\\{\\{secret:(?<name>[^}]+)\\}\\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string _storePath;

    public LauncherSecretStoreService(string? storePath = null)
    {
        _storePath = string.IsNullOrWhiteSpace(storePath)
            ? Path.Combine(GetLauncherDataDirectory(), "launcher-secrets.json")
            : storePath;
    }

    public string StorePath => _storePath;

    public static string BuildToken(string secretName)
    {
        return $"{{{{secret:{secretName}}}}}";
    }

    public bool SaveSecret(string secretName, string secretValue)
    {
        if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretValue))
        {
            return false;
        }

        try
        {
            var state = LoadState();
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secretValue),
                null,
                DataProtectionScope.CurrentUser);

            state.Secrets[secretName.Trim()] = Convert.ToBase64String(protectedBytes);
            SaveState(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetSecretNames()
    {
        var state = LoadState();
        return state.Secrets.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool DeleteSecret(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return false;
        }

        try
        {
            var state = LoadState();
            var removed = state.Secrets.Remove(secretName.Trim());
            if (!removed)
            {
                return false;
            }

            SaveState(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RenameSecret(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        oldName = oldName.Trim();
        newName = newName.Trim();
        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var state = LoadState();
            if (!state.Secrets.TryGetValue(oldName, out var value))
            {
                return false;
            }

            if (state.Secrets.ContainsKey(newName))
            {
                return false;
            }

            state.Secrets.Remove(oldName);
            state.Secrets[newName] = value;
            SaveState(state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetSecret(string secretName, out string secretValue)
    {
        secretValue = string.Empty;
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return false;
        }

        try
        {
            var state = LoadState();
            if (!state.Secrets.TryGetValue(secretName.Trim(), out var stored))
            {
                return false;
            }

            var protectedBytes = Convert.FromBase64String(stored);
            var unprotectedBytes = ProtectedData.Unprotect(
                protectedBytes,
                null,
                DataProtectionScope.CurrentUser);

            secretValue = Encoding.UTF8.GetString(unprotectedBytes);
            return !string.IsNullOrWhiteSpace(secretValue);
        }
        catch
        {
            secretValue = string.Empty;
            return false;
        }
    }

    public bool ResolveSecretTokens(string input, out string resolved, out IReadOnlyList<string> missingSecretNames)
    {
        resolved = input;
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(input))
        {
            missingSecretNames = missing;
            return true;
        }

        resolved = SecretTokenRegex.Replace(input, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            if (TryGetSecret(name, out var value))
            {
                return value;
            }

            if (!missing.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(name);
            }

            return match.Value;
        });

        missingSecretNames = missing;
        return missing.Count == 0;
    }

    private static string GetLauncherDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private SecretStoreState LoadState()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new SecretStoreState();
            }

            var json = File.ReadAllText(_storePath);
            var state = JsonSerializer.Deserialize<SecretStoreState>(json);
            return state ?? new SecretStoreState();
        }
        catch
        {
            return new SecretStoreState();
        }
    }

    private void SaveState(SecretStoreState state)
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }

    private sealed class SecretStoreState
    {
        public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
