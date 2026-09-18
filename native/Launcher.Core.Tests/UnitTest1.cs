using Launcher.Core.Models;
using Launcher.Core.Services;
using System.Text.Json.Nodes;
using Xunit;

namespace Launcher.Core.Tests;

public class LauncherCoreTests
{
    [Fact]
    public async Task NativeStartRunner_SkipUpdateTable_BypassesConfiguredFlow()
    {
        var output = new List<string>();
        var document = new LauncherConfigDocument
        {
            FilePath = Path.Combine(Path.GetTempPath(), "launcher.config.json"),
            Root = new JsonObject(),
            Configuration = new LauncherConfiguration
            {
                EnsureCapsLockOn = false,
                EnsureNumLockOn = false,
                Steps =
                {
                    new LauncherStep
                    {
                        Name = "Visual Board",
                        Type = "launch",
                        ProgramPath = "visual-board.exe",
                        LaunchOnlyIfMissing = false,
                        PostLaunchDelaySeconds = 0,
                        UpdateTableFlow = new LauncherUpdateTableFlow()
                    }
                }
            }
        };

        await new LauncherNativeStartRunner().RunAsync(document, true, output.Add, skipUpdateTable: true);

        Assert.Contains("Skipping Update Table flow for 'Visual Board' by user request.", output);
    }

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
    public void BuildRunningWindowTitleCandidates_PrefersExplicitRunningTitles_OverFallbackAliases()
    {
        var step = new LauncherStep
        {
            Name = "Visual Mfg",
            Type = "launch",
            ProgramPath = @"\\INFOR-VMSERVER\Visual1000$\VMFG\VM.EXE",
            WindowTitle = "Visual Manufacturing",
            RunningWindowTitles = { "Visual Manufacturing", "VMFG" },
            FallbackWindowTitles = { "Visual", "VMFG", "VM" }
        };

        var titles = LauncherNativeDetectionService.BuildRunningWindowTitleCandidatesForTests(step);

        Assert.Contains("Visual Manufacturing", titles);
        Assert.Contains("VMFG", titles);
        Assert.DoesNotContain("Visual", titles);
        Assert.DoesNotContain("VM", titles);
    }

    [Fact]
    public void ResolveDirectoryLaunchTargetPath_CreatesMonthAndDateFolders_WhenConfigured()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"launcher-receiver-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(baseDirectory);

            var step = new LauncherStep
            {
                Name = "Receiver's",
                ProgramPath = baseDirectory,
                EnsureCurrentMonthFolder = true,
                EnsureCurrentDateFolder = true,
                CurrentMonthFolderFormat = "MMMM yyyy",
                CurrentDateFolderFormat = "MM_dd_yyyy"
            };

            var target = LauncherNativeStartRunner.ResolveDirectoryLaunchTargetPathForTests(step, baseDirectory, createMissing: true, new DateTime(2026, 9, 18));
            var expected = Path.Combine(baseDirectory, "September 2026", "09_18_2026");

            Assert.Equal(expected, target);
            Assert.True(Directory.Exists(Path.Combine(baseDirectory, "September 2026")));
            Assert.True(Directory.Exists(expected));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ShouldOpenBaseDirectoryAfterLaunch_ReturnsTrue_WhenBaseAndDateFoldersDiffer()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"launcher-receiver-base-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(baseDirectory);
            var launchTarget = Path.Combine(baseDirectory, "September 2026", "09_18_2026");
            Directory.CreateDirectory(launchTarget);

            var step = new LauncherStep
            {
                Name = "Receiver's",
                ProgramPath = baseDirectory,
                OpenBaseDirectoryAfterLaunch = true
            };

            Assert.True(LauncherNativeStartRunner.ShouldOpenBaseDirectoryAfterLaunchForTests(step, baseDirectory, launchTarget));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
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

    [Fact]
    public void UserProfileService_CreateAuthenticateAndRecordLogin_Works()
    {
        var userStorePath = Path.Combine(Path.GetTempPath(), $"launcher-users-{Guid.NewGuid():N}.json");
        var secretStorePath = Path.Combine(Path.GetTempPath(), $"launcher-user-secrets-{Guid.NewGuid():N}.json");

        try
        {
            var secretStore = new LauncherSecretStoreService(secretStorePath);
            var service = new LauncherUserProfileService(userStorePath, secretStore);

            var input = new LauncherCreateUserInput
            {
                UserName = "judson",
                DisplayName = "Judson Fitzpatrick",
                Email = "judson@example.com",
                Department = "Operations",
                Notes = "Primary user",
                Password = "abc123"
            };

            Assert.True(service.CreateUser(input, out var createError), createError);
            Assert.Single(service.GetUsers());
            Assert.True(service.TryAuthenticate("judson", "abc123", out var user, out var authError), authError);
            Assert.NotNull(user);

            service.RecordLogin("judson", "Needs a faster update flow.");

            var savedUser = service.GetUsers().Single();
            Assert.Equal(1, savedUser.LoginCount);
            Assert.Single(savedUser.LoginHistory);
            Assert.Single(savedUser.IssueHistory);
            Assert.Equal("Needs a faster update flow.", savedUser.IssueHistory[0].Summary);
        }
        finally
        {
            if (File.Exists(userStorePath))
            {
                File.Delete(userStorePath);
            }

            if (File.Exists(secretStorePath))
            {
                File.Delete(secretStorePath);
            }
        }
    }

    [Fact]
    public void UserProfileService_UpdateUser_UpdatesCredentialsAndProfile()
    {
        var userStorePath = Path.Combine(Path.GetTempPath(), $"launcher-users-{Guid.NewGuid():N}.json");
        var secretStorePath = Path.Combine(Path.GetTempPath(), $"launcher-user-secrets-{Guid.NewGuid():N}.json");

        try
        {
            var secretStore = new LauncherSecretStoreService(secretStorePath);
            var service = new LauncherUserProfileService(userStorePath, secretStore);

            var createInput = new LauncherCreateUserInput
            {
                UserName = "judson",
                DisplayName = "Judson Fitzpatrick",
                Password = "abc123"
            };

            Assert.True(service.CreateUser(createInput, out var createError), createError);

            var updateInput = new LauncherUpdateUserInput
            {
                OriginalUserName = "judson",
                NewUserName = "judsonf",
                DisplayName = "Judson F.",
                Email = "judsonf@example.com",
                Department = "Operations",
                Notes = "Updated profile",
                CurrentPassword = "abc123",
                NewPassword = "xyz789"
            };

            Assert.True(service.UpdateUser(updateInput, out var updateError), updateError);
            Assert.False(service.TryAuthenticate("judson", "abc123", out _, out _));
            Assert.True(service.TryAuthenticate("judsonf", "xyz789", out var updatedUser, out var authError), authError);
            Assert.NotNull(updatedUser);
            Assert.Equal("Judson F.", updatedUser!.DisplayName);
            Assert.Equal("judsonf@example.com", updatedUser.Email);
            Assert.Equal("Operations", updatedUser.Department);
            Assert.Equal("Updated profile", updatedUser.Notes);
        }
        finally
        {
            if (File.Exists(userStorePath))
            {
                File.Delete(userStorePath);
            }

            if (File.Exists(secretStorePath))
            {
                File.Delete(secretStorePath);
            }
        }
    }
}