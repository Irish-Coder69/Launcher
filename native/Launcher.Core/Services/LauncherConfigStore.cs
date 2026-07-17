using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherConfigStore
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public LauncherConfigDocument Load(string configPath)
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
        var rootNode = JsonNode.Parse(json);
        if (rootNode is not JsonObject rootObject)
        {
            throw new InvalidOperationException("Launcher config root JSON value must be an object.");
        }

        var config = rootObject.Deserialize<LauncherConfiguration>(DeserializeOptions);
        if (config is null)
        {
            throw new InvalidOperationException("Launcher config file could not be parsed.");
        }

        return new LauncherConfigDocument
        {
            FilePath = configPath,
            Root = rootObject,
            Configuration = config
        };
    }

    public void ApplyGlobalSettings(LauncherConfigDocument document, LauncherSettingsInput settings)
    {
        document.Root["checkForUpdates"] = settings.CheckForUpdates;
        document.Root["ensureCapsLockOn"] = settings.EnsureCapsLockOn;
        document.Root["ensureNumLockOn"] = settings.EnsureNumLockOn;

        var closeOptions = document.Root["closeOptions"] as JsonObject ?? new JsonObject();
        closeOptions["closeOnlyTrackedApps"] = settings.CloseOnlyTrackedApps;
        closeOptions["defaultCloseMethod"] = settings.DefaultCloseMethod;
        closeOptions["defaultCloseTimeoutSeconds"] = settings.DefaultCloseTimeoutSeconds;
        closeOptions["defaultCloseForce"] = settings.DefaultCloseForce;
        document.Root["closeOptions"] = closeOptions;
    }

    public void ApplyStepEnabledStates(LauncherConfigDocument document, IReadOnlyDictionary<string, bool> enabledByStepName)
    {
        if (document.Root["steps"] is not JsonArray stepsArray)
        {
            return;
        }

        foreach (var node in stepsArray)
        {
            if (node is not JsonObject stepObject)
            {
                continue;
            }

            var stepName = stepObject["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(stepName))
            {
                continue;
            }

            if (enabledByStepName.TryGetValue(stepName, out var enabled))
            {
                stepObject["enabled"] = enabled;
            }
        }
    }

    public void Save(LauncherConfigDocument document)
    {
        var json = document.Root.ToJsonString(SerializeOptions);
        File.WriteAllText(document.FilePath, json);
    }
}
