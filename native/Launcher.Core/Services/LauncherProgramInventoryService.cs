using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Launcher.Core.Services;

public sealed class LauncherProgramInventoryService
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly string _cacheFilePath;

    public LauncherProgramInventoryService()
    {
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Launcher");

        Directory.CreateDirectory(rootDirectory);
        _cacheFilePath = Path.Combine(rootDirectory, "program-inventory.json");
    }

    public string CacheFilePath => _cacheFilePath;

    public async Task<IReadOnlyList<LauncherProgramInventoryEntry>> GetProgramsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            var cached = LoadCacheIfFresh();
            if (cached is not null)
            {
                return cached;
            }
        }

        var entries = await BuildInventoryAsync(cancellationToken).ConfigureAwait(false);
        SaveCache(entries);
        return entries;
    }

    public async Task RefreshInventoryAsync(CancellationToken cancellationToken = default)
    {
        var entries = await BuildInventoryAsync(cancellationToken).ConfigureAwait(false);
        SaveCache(entries);
    }

    public IReadOnlyList<LauncherProgramInventoryEntry> GetCachedPrograms()
    {
        return LoadCacheIfFresh() ?? Array.Empty<LauncherProgramInventoryEntry>();
    }

    private IReadOnlyList<LauncherProgramInventoryEntry>? LoadCacheIfFresh()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var cache = JsonSerializer.Deserialize<LauncherProgramInventoryCache>(json);
            if (cache is null || cache.SchemaVersion != SchemaVersion)
            {
                return null;
            }

            var age = DateTimeOffset.UtcNow - cache.CachedAtUtc;
            if (age > CacheTtl)
            {
                return null;
            }

            return cache.Entries
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ProgramPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(IReadOnlyCollection<LauncherProgramInventoryEntry> entries)
    {
        var cache = new LauncherProgramInventoryCache
        {
            SchemaVersion = SchemaVersion,
            CachedAtUtc = DateTimeOffset.UtcNow,
            Entries = entries.ToList()
        };

        var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_cacheFilePath, json);
    }

    private async Task<IReadOnlyList<LauncherProgramInventoryEntry>> BuildInventoryAsync(CancellationToken cancellationToken)
    {
        var results = new List<LauncherProgramInventoryEntry>();

        results.AddRange(GetRegistryInstalledPrograms());
        results.AddRange(GetUwpPackages(cancellationToken));
        results.AddRange(GetStartMenuPrograms());
        results.AddRange(GetPathPrograms());

        await Task.CompletedTask.ConfigureAwait(false);

        return DeduplicateAndRank(results);
    }

    private static IReadOnlyList<LauncherProgramInventoryEntry> GetRegistryInstalledPrograms()
    {
        var results = new List<LauncherProgramInventoryEntry>();
        var uninstallRoots = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")
        };

        foreach (var (hive, view, subKeyPath) in uninstallRoots)
        {
            RegistryKey? uninstallKey = null;
            try
            {
                uninstallKey = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(subKeyPath);
            }
            catch
            {
                uninstallKey = null;
            }

            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var productSubKeyName in uninstallKey.GetSubKeyNames())
            {
                RegistryKey? productKey = null;
                try
                {
                    productKey = uninstallKey.OpenSubKey(productSubKeyName);
                    if (productKey is null)
                    {
                        continue;
                    }

                    var displayName = productKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var displayIcon = NormalizeExecutablePath(productKey.GetValue("DisplayIcon") as string);
                    var installLocation = productKey.GetValue("InstallLocation") as string;
                    var executablePath = ResolveExecutablePath(displayIcon, installLocation);

                    results.Add(new LauncherProgramInventoryEntry
                    {
                        DisplayName = displayName.Trim(),
                        ProgramPath = executablePath,
                        Source = "Registry",
                        AppId = productSubKeyName,
                        Publisher = (productKey.GetValue("Publisher") as string)?.Trim(),
                        Version = (productKey.GetValue("DisplayVersion") as string)?.Trim()
                    });
                }
                catch
                {
                    // Skip unreadable registry entries.
                }
                finally
                {
                    productKey?.Dispose();
                }
            }

            uninstallKey.Dispose();
        }

        return results;
    }

    private static IReadOnlyList<LauncherProgramInventoryEntry> GetUwpPackages(CancellationToken cancellationToken)
    {
        var results = new List<LauncherProgramInventoryEntry>();
        var script = "$ErrorActionPreference='Stop'; Get-AppxPackage | Select-Object Name, PackageFamilyName, InstallLocation, Version | ConvertTo-Json -Depth 3";
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return results;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return results;
            }

            var root = JsonNode.Parse(output);
            if (root is null)
            {
                return results;
            }

            foreach (var packageNode in EnumerateJsonNodes(root))
            {
                var name = packageNode?["Name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var installLocation = packageNode?["InstallLocation"]?.GetValue<string>();
                var packageFamilyName = packageNode?["PackageFamilyName"]?.GetValue<string>();
                var version = packageNode?["Version"]?.GetValue<string>();

                results.Add(new LauncherProgramInventoryEntry
                {
                    DisplayName = name.Trim(),
                    ProgramPath = NormalizeDirectoryPath(installLocation),
                    Source = "UWP",
                    AppId = packageFamilyName,
                    Version = version
                });
            }
        }
        catch
        {
            return results;
        }
        finally
        {
            process?.Dispose();
        }

        return results;
    }

    private static IEnumerable<JsonNode?> EnumerateJsonNodes(JsonNode root)
    {
        if (root is JsonArray array)
        {
            foreach (var node in array)
            {
                yield return node;
            }

            yield break;
        }

        yield return root;
    }

    private static IReadOnlyList<LauncherProgramInventoryEntry> GetStartMenuPrograms()
    {
        var results = new List<LauncherProgramInventoryEntry>();
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

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
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var targetPath = filePath;
                if (shell is not null && filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        dynamic shortcut = shell.CreateShortcut(filePath);
                        targetPath = shortcut.TargetPath as string ?? string.Empty;
                    }
                    catch
                    {
                        continue;
                    }
                }

                var normalizedTargetPath = NormalizeExecutablePath(targetPath);
                if (string.IsNullOrWhiteSpace(normalizedTargetPath))
                {
                    continue;
                }

                results.Add(new LauncherProgramInventoryEntry
                {
                    DisplayName = displayName,
                    ProgramPath = normalizedTargetPath,
                    Source = "StartMenu"
                });
            }
        }

        return results;
    }

    private static IReadOnlyList<LauncherProgramInventoryEntry> GetPathPrograms()
    {
        var results = new List<LauncherProgramInventoryEntry>();
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                results.Add(new LauncherProgramInventoryEntry
                {
                    DisplayName = displayName,
                    ProgramPath = NormalizeExecutablePath(filePath),
                    Source = "PATH"
                });
            }
        }

        return results;
    }

    private static IReadOnlyList<LauncherProgramInventoryEntry> DeduplicateAndRank(IEnumerable<LauncherProgramInventoryEntry> items)
    {
        var rankedSourceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Registry"] = 1,
            ["UWP"] = 2,
            ["StartMenu"] = 3,
            ["PATH"] = 4,
            ["RunningProcess"] = 5
        };

        var deduped = items
            .Where(item => !string.IsNullOrWhiteSpace(item.DisplayName))
            .GroupBy(item => BuildDeduplicationKey(item), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => rankedSourceOrder.TryGetValue(item.Source, out var rank) ? rank : int.MaxValue)
                .ThenBy(item => string.IsNullOrWhiteSpace(item.ProgramPath) ? 1 : 0)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return deduped;
    }

    private static string BuildDeduplicationKey(LauncherProgramInventoryEntry item)
    {
        var byPath = NormalizeExecutablePath(item.ProgramPath);
        if (!string.IsNullOrWhiteSpace(byPath))
        {
            return byPath;
        }

        var appId = item.AppId?.Trim();
        if (!string.IsNullOrWhiteSpace(appId))
        {
            return appId;
        }

        return (item.DisplayName ?? string.Empty).Trim();
    }

    private static string? ResolveExecutablePath(string? displayIcon, string? installLocation)
    {
        var iconPath = NormalizeExecutablePath(displayIcon);
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            return iconPath;
        }

        var directory = NormalizeDirectoryPath(installLocation);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var firstExe = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            return NormalizeExecutablePath(firstExe);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeDirectoryPath(string? value)
    {
        var trimmed = value?.Trim().Trim('\"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeExecutablePath(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        // Strip command-line tail and icon index suffix if present.
        var candidate = trimmed.Trim('"');
        var commaIndex = candidate.IndexOf(',');
        if (commaIndex > 0)
        {
            candidate = candidate[..commaIndex];
        }

        if (candidate.StartsWith("\"", StringComparison.Ordinal) && candidate.Contains("\" ", StringComparison.Ordinal))
        {
            candidate = candidate.Trim('"');
        }

        if (candidate.StartsWith("\\\\", StringComparison.Ordinal) || Path.IsPathRooted(candidate))
        {
            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private sealed class LauncherProgramInventoryCache
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset CachedAtUtc { get; set; }

        public List<LauncherProgramInventoryEntry> Entries { get; set; } = new();
    }
}

public sealed class LauncherProgramInventoryEntry
{
    public string DisplayName { get; set; } = string.Empty;

    public string? ProgramPath { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? AppId { get; set; }

    public string? Publisher { get; set; }

    public string? Version { get; set; }
}
