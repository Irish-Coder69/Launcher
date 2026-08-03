using Launcher.Core.Models;
using Launcher.Core.Services;
using Xunit;

namespace Launcher.Core.Tests;

public class LauncherCoreTests
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

    [Fact]
    public void LearningService_ReturnsEmptyRecommendation_WhenRunThresholdNotMet()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"launcher-learning-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherLearningService(statePath);
            service.RecordRun(new[] { "Outlook", "Visual Board" });

            var recommendation = service.GetRecommendedOrder(new[] { "Outlook", "Visual Board" }, minRunsBeforeSuggestions: 2);

            Assert.Empty(recommendation);
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }
        }
    }

    [Fact]
    public void LearningService_PrefersMostLikelyLearnedFlow()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"launcher-learning-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherLearningService(statePath);
            service.RecordRun(new[] { "Outlook", "Local Visual Board", "Stockroom Analytics" });
            service.RecordRun(new[] { "Outlook", "Local Visual Board", "Stockroom Analytics" });
            service.RecordRun(new[] { "Outlook", "Stockroom Analytics" });

            var recommendation = service.GetRecommendedOrder(
                new[] { "Stockroom Analytics", "Local Visual Board", "Outlook" },
                minRunsBeforeSuggestions: 3);

            Assert.Equal(new[] { "Outlook", "Local Visual Board", "Stockroom Analytics" }, recommendation);
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }
        }
    }

    [Fact]
    public void LearningService_ResetHistory_RemovesStateFile()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"launcher-learning-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherLearningService(statePath);
            service.RecordRun(new[] { "Outlook" });

            Assert.True(File.Exists(statePath));
            Assert.True(service.ResetHistory());
            Assert.False(File.Exists(statePath));
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }
        }
    }

    [Fact]
    public void SecretStoreService_SaveAndResolveToken_ReturnsSecretValue()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"launcher-secrets-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherSecretStoreService(storePath);
            Assert.True(service.SaveSecret("DbPassword", "abc123"));

            var input = "{TAB}{{secret:DbPassword}}{ENTER}";
            Assert.True(service.ResolveSecretTokens(input, out var resolved, out var missing));
            Assert.Empty(missing);
            Assert.Equal("{TAB}abc123{ENTER}", resolved);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void SecretStoreService_ResolveToken_ReportsMissingSecret()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"launcher-secrets-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherSecretStoreService(storePath);
            var input = "{{secret:MissingSecret}}";

            Assert.False(service.ResolveSecretTokens(input, out var resolved, out var missing));
            Assert.Single(missing);
            Assert.Equal("MissingSecret", missing[0]);
            Assert.Equal(input, resolved);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void SecretStoreService_ListRenameDelete_Works()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"launcher-secrets-{Guid.NewGuid():N}.json");

        try
        {
            var service = new LauncherSecretStoreService(storePath);
            Assert.True(service.SaveSecret("Alpha", "one"));
            Assert.True(service.SaveSecret("Beta", "two"));

            var namesBefore = service.GetSecretNames();
            Assert.Contains("Alpha", namesBefore);
            Assert.Contains("Beta", namesBefore);

            Assert.True(service.RenameSecret("Alpha", "Gamma"));
            Assert.False(service.RenameSecret("Beta", "Gamma"));

            var namesAfterRename = service.GetSecretNames();
            Assert.DoesNotContain("Alpha", namesAfterRename);
            Assert.Contains("Gamma", namesAfterRename);

            Assert.True(service.DeleteSecret("Beta"));
            Assert.False(service.DeleteSecret("Missing"));

            var namesAfterDelete = service.GetSecretNames();
            Assert.DoesNotContain("Beta", namesAfterDelete);
            Assert.Contains("Gamma", namesAfterDelete);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }
}