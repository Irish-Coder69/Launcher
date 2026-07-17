using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public LauncherConfiguration Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Config path is required.", nameof(configPath));
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Launcher config file was not found.", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<LauncherConfiguration>(json, JsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException("Launcher config file could not be parsed.");
        }

        return config;
    }
}
