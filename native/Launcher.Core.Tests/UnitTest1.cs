using Launcher.Core.Models;
using Launcher.Core.Services;
using Xunit;

namespace Launcher.Core.Tests;

public class LauncherNativeStartRunnerTests
{
    [Fact]
    public void TryGetFirstTextLoginValue_UsesConfiguredLoginFieldValue_WhenPresent()
    {
        var entries = new List<LauncherKeySequenceEntry>
        {
            new() { Keys = "9563", DelayMs = 900 },
            new() { Keys = "{ENTER}", DelayMs = 2000 }
        };

        var value = LauncherNativeStartRunner.TryGetFirstTextLoginValueForTests(entries);

        Assert.Equal("9563", value);
    }
}