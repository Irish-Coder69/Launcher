using System.Text.Json.Nodes;

namespace Launcher.Core.Models;

public sealed class LauncherConfigDocument
{
    public required string FilePath { get; init; }

    public required JsonObject Root { get; init; }

    public required LauncherConfiguration Configuration { get; init; }
}

public sealed class LauncherSettingsInput
{
    public bool CheckForUpdates { get; set; }

    public bool EnsureCapsLockOn { get; set; }

    public bool EnsureNumLockOn { get; set; }

    public bool CloseOnlyTrackedApps { get; set; }

    public string DefaultCloseMethod { get; set; } = "both";

    public int DefaultCloseTimeoutSeconds { get; set; } = 12;

    public bool DefaultCloseForce { get; set; }
}
