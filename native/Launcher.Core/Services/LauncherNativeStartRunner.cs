using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherNativeStartRunner
{
    private readonly LauncherNativeDetectionService _detectionService = new();

    public async Task RunAsync(
        LauncherConfigDocument document,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default)
    {
        var config = document.Configuration;
        var configDirectory = Path.GetDirectoryName(document.FilePath) ?? Environment.CurrentDirectory;
        var launchedSteps = new List<LauncherSessionStep>();

        foreach (var step in config.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!step.Enabled)
            {
                Log(onOutput, $"Skipping disabled step '{step.Name}'");
                continue;
            }

            LogStepHeader(onOutput, step.Name);

            switch ((step.Type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "launch":
                    await RunLaunchStepAsync(step, configDirectory, dryRun, onOutput, launchedSteps, cancellationToken);
                    break;
                case "access-sql":
                    if (dryRun)
                    {
                        Log(onOutput, $"[DryRun] Would execute {step.Sql.Count} SQL statement(s) for '{step.Name}'");
                        break;
                    }

                    throw new NotSupportedException($"Native start mode does not yet support access-sql step '{step.Name}'.");
                default:
                    throw new NotSupportedException($"Unsupported step type '{step.Type}' in step '{step.Name}'.");
            }

            var waitAfterStepSeconds = step.WaitAfterStepSeconds ?? 0;
            if (waitAfterStepSeconds > 0)
            {
                if (dryRun)
                {
                    Log(onOutput, $"[DryRun] Would wait {waitAfterStepSeconds} second(s) after step '{step.Name}'");
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitAfterStepSeconds), cancellationToken);
                }
            }
        }

        if (dryRun)
        {
            Log(onOutput, "[DryRun] Would record launcher session state for close mode");
        }
        else
        {
            SaveLauncherSessionState(document.FilePath, launchedSteps);
        }

        await EnsureLockKeysOnAsync(config, dryRun, onOutput, cancellationToken);
        Log(onOutput, "Launcher sequence completed.");
    }

    private async Task RunLaunchStepAsync(
        LauncherStep step,
        string configDirectory,
        bool dryRun,
        Action<string>? onOutput,
        List<LauncherSessionStep> launchedSteps,
        CancellationToken cancellationToken)
    {
        var launchOnlyIfMissing = step.LaunchOnlyIfMissing ?? true;
        var runningBeforeLaunch = false;

        if (launchOnlyIfMissing)
        {
            runningBeforeLaunch = _detectionService.IsStepRunning(step);
            Log(onOutput, $"Checked '{step.Name}': {(runningBeforeLaunch ? "already running" : "not running")}");

            if (runningBeforeLaunch)
            {
                Log(onOutput, dryRun
                    ? $"[DryRun] Would skip '{step.Name}' because it is already running"
                    : $"Skipping '{step.Name}' launch because it is already running");
                return;
            }
        }

        var beforeProcessIds = GetTrackedProcessIds(step);
        Process? process = null;

        if (dryRun)
        {
            Log(onOutput, $"[DryRun] Would launch: {GetDryRunTarget(step, configDirectory)} {step.Arguments ?? string.Empty} (WindowStyle={ResolveWindowStyle(step)})");
        }
        else
        {
            process = StartStepProcess(step, configDirectory);
            Log(onOutput, $"Launching '{step.Name}'");
        }

        var postLaunchDelaySeconds = step.PostLaunchDelaySeconds ?? 3;
        if (postLaunchDelaySeconds > 0)
        {
            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would wait {postLaunchDelaySeconds} second(s) after launching '{step.Name}'");
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(postLaunchDelaySeconds), cancellationToken);
            }
        }

        await InvokeMinimizeLaunchedWindowAsync(step, process, afterCompletion: false, dryRun, onOutput, cancellationToken);
        await WaitForWindowToCloseAsync(step, dryRun, onOutput, cancellationToken);
        await InvokePreLoginWindowPreparationAsync(step, process, dryRun, onOutput, cancellationToken);
        await SendLoginSequenceAsync(step, process, dryRun, onOutput, cancellationToken);

        var loginCompleteWait = step.WaitForLoginCompleteSeconds ?? 0;
        if (loginCompleteWait > 0)
        {
            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would wait {loginCompleteWait} second(s) for login to complete");
            }
            else
            {
                Log(onOutput, $"Waiting {loginCompleteWait} second(s) for login to complete");
                await Task.Delay(TimeSpan.FromSeconds(loginCompleteWait), cancellationToken);
            }
        }

        await ConfirmLoginCompletionAsync(step, process, dryRun, onOutput, cancellationToken);
        await InvokeMoveWindowToMonitorAsync(step, process, beforeLogin: false, dryRun, onOutput, cancellationToken);
        await InvokeUpdateTableFlowAsync(step, process, dryRun, onOutput, cancellationToken);
        await InvokeMinimizeLaunchedWindowAsync(step, process, afterCompletion: true, dryRun, onOutput, cancellationToken);
        await InvokeMinimizeAdditionalWindowTitlesAfterCompletionAsync(step, dryRun, onOutput, cancellationToken);

        if (!runningBeforeLaunch)
        {
            var afterProcessIds = GetTrackedProcessIds(step);
            var newProcessIds = afterProcessIds.Except(beforeProcessIds).Distinct().ToList();
            if (newProcessIds.Count == 0)
            {
                newProcessIds = afterProcessIds;
            }

            launchedSteps.Add(new LauncherSessionStep
            {
                StepName = step.Name,
                StepType = step.Type,
                ProcessIds = newProcessIds,
                Closed = false,
                LaunchedAt = DateTimeOffset.Now.ToString("O")
            });
        }
    }

    private static async Task EnsureLockKeysOnAsync(
        LauncherConfiguration config,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            if (config.EnsureCapsLockOn)
            {
                Log(onOutput, "[DryRun] Would ensure Caps Lock is ON");
            }

            if (config.EnsureNumLockOn)
            {
                Log(onOutput, "[DryRun] Would ensure Num Lock is ON");
            }

            return;
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (config.EnsureCapsLockOn && !Control.IsKeyLocked(Keys.CapsLock))
        {
            SendKeys.SendWait("{CAPSLOCK}");
            Log(onOutput, Control.IsKeyLocked(Keys.CapsLock) ? "Caps Lock is ON" : "Could not confirm Caps Lock is ON");
        }

        if (config.EnsureNumLockOn && !Control.IsKeyLocked(Keys.NumLock))
        {
            SendKeys.SendWait("{NUMLOCK}");
            Log(onOutput, Control.IsKeyLocked(Keys.NumLock) ? "Num Lock is ON" : "Could not confirm Num Lock is ON");
        }
    }

    private static async Task SendLoginSequenceAsync(
        LauncherStep step,
        Process? process,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        if (step.LoginSequence.Count == 0)
        {
            return;
        }

        if (dryRun)
        {
            Log(onOutput, $"[DryRun] Would run login sequence for '{step.Name}'");
            return;
        }

        var loginTitle = string.IsNullOrWhiteSpace(step.LoginWindowTitle) ? step.WindowTitle : step.LoginWindowTitle;
        if (string.IsNullOrWhiteSpace(loginTitle))
        {
            throw new InvalidOperationException($"Step '{step.Name}' has loginSequence but no window title.");
        }

        var loginTitles = DistinctTitles(
            new[] { loginTitle },
            step.LoginFallbackWindowTitles);

        var activationTitles = DistinctTitles(
            new[] { loginTitle, step.WindowTitle },
            step.LoginFallbackWindowTitles,
            step.FallbackWindowTitles);

        var activated = await TryActivateWindowAsync(loginTitles, process?.Id, step.WindowTimeoutSeconds ?? 30, 1000, cancellationToken);
        if (!activated)
        {
            activated = await TryActivateWindowAsync(activationTitles, process?.Id, step.WindowTimeoutSeconds ?? 30, 1000, cancellationToken);
            if (activated)
            {
                Log(onOutput, $"Could not activate strict login window for '{step.Name}'; using fallback title match.");
            }
        }

        if (!activated)
        {
            throw new InvalidOperationException($"Could not activate login window for '{step.Name}'.");
        }

        var loginWindowHandle = TryFindFirstWindow(loginTitles, process?.Id);
        if (loginWindowHandle == IntPtr.Zero)
        {
            loginWindowHandle = TryFindFirstWindow(activationTitles, process?.Id);
        }

        var preferredNames = step.LoginFieldPreferredNames.Count > 0
            ? step.LoginFieldPreferredNames
            : new List<string> { "Enter employeeID", "Enter employee ID", "Employee ID", "Employee", "EmployeeId", "Employee Id", "Login" };
        var excludedNames = step.LoginFieldExcludeNames.Count > 0
            ? step.LoginFieldExcludeNames
            : new List<string> { "Help", "Search", "Tell me", "Find" };
        var requireFieldConfirmation = step.LoginFieldRequireConfirmation ?? false;
        var readyTimeoutSeconds = step.LoginFieldReadyTimeoutSeconds ?? 10;
        var fieldReady = false;
        var clearKeys = string.IsNullOrWhiteSpace(step.LoginFieldFallbackClearKeys)
            ? "^a{BACKSPACE}"
            : step.LoginFieldFallbackClearKeys;

        if (loginWindowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindowAsync(loginWindowHandle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(loginWindowHandle);
            await Task.Delay(250, cancellationToken);

            for (var attempt = 0; attempt < Math.Max(1, readyTimeoutSeconds * 2); attempt++)
            {
                if (TryGetAutomationElement(loginWindowHandle, out var windowElement) &&
                    windowElement is not null &&
                    TryFocusLoginField(windowElement, preferredNames, excludedNames, out _))
                {
                    fieldReady = true;
                    break;
                }

                await Task.Delay(500, cancellationToken);
            }
        }

        if (!fieldReady && requireFieldConfirmation)
        {
            throw new InvalidOperationException($"Could not confirm login field readiness for '{step.Name}'.");
        }

        var inputValue = !string.IsNullOrWhiteSpace(step.LoginFieldValue)
            ? step.LoginFieldValue
            : TryGetFirstTextLoginValue(step.LoginSequence);

        if (!string.IsNullOrWhiteSpace(inputValue))
        {
            var preKeys = step.LoginFieldFallbackPreKeys;
            foreach (var preKey in preKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                SendKeys.SendWait(preKey);
                await Task.Delay(300, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(clearKeys))
            {
                SendKeys.SendWait(clearKeys);
                await Task.Delay(250, cancellationToken);
            }

            if (loginWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(loginWindowHandle);
                await Task.Delay(150, cancellationToken);
            }

            SendKeys.SendWait(inputValue);
            await Task.Delay(step.LoginFieldFallbackValueDelayMs ?? 700, cancellationToken);
        }

        var entriesToSend = step.LoginSequence.ToList();
        if (!string.IsNullOrWhiteSpace(inputValue) && entriesToSend.Count > 0)
        {
            var firstEntry = entriesToSend[0];
            if (IsSimpleTextInput(firstEntry.Keys) && string.Equals(firstEntry.Keys, inputValue, StringComparison.Ordinal))
            {
                entriesToSend = entriesToSend.Skip(1).ToList();
            }
        }

        foreach (var entry in entriesToSend)
        {
            if (string.IsNullOrWhiteSpace(entry.Keys))
            {
                continue;
            }

            if (loginWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(loginWindowHandle);
            }

            SendKeys.SendWait(entry.Keys);
            await Task.Delay(entry.DelayMs ?? 700, cancellationToken);
        }

        if (step.LoginSequence.Any(entry => !string.IsNullOrWhiteSpace(entry.Keys) && entry.Keys.ToUpperInvariant().Contains("ENTER")))
        {
            var retryCount = step.LoginEnterRetryCount ?? 3;
            var retryDelayMs = step.LoginEnterRetryDelayMs ?? 800;
            var reattemptCount = step.LoginReattemptCount ?? 1;
            var failIfWindowStillActive = step.LoginFailIfWindowStillActive ?? true;
            var loginSucceeded = false;
            var mainTitles = DistinctTitles(
                new[] { string.IsNullOrWhiteSpace(step.LoginSuccessWindowTitle) ? step.WindowTitle : step.LoginSuccessWindowTitle },
                step.LoginSuccessFallbackWindowTitles,
                step.FallbackWindowTitles);

            for (var retry = 0; retry < retryCount; retry++)
            {
                await Task.Delay(retryDelayMs, cancellationToken);
                var loginWindowStillActive = HasAnyWindow(loginTitles, process?.Id);
                var mainWindowVisible = HasAnyWindow(mainTitles, process?.Id);
                if (!loginWindowStillActive || mainWindowVisible)
                {
                    loginSucceeded = true;
                    break;
                }

                if (loginWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(loginWindowHandle);
                }

                SendKeys.SendWait("{ENTER}");
            }

            if (!loginSucceeded && !string.IsNullOrWhiteSpace(inputValue))
            {
                for (var attempt = 0; attempt < reattemptCount; attempt++)
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                    var loginWindowStillActive = HasAnyWindow(loginTitles, process?.Id);
                    var mainWindowVisible = HasAnyWindow(mainTitles, process?.Id);
                    if (!loginWindowStillActive || mainWindowVisible)
                    {
                        loginSucceeded = true;
                        break;
                    }

                    if (loginWindowHandle != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(loginWindowHandle);
                    }

                    if (loginWindowHandle != IntPtr.Zero && TryGetAutomationElement(loginWindowHandle, out var windowElement) &&
                        windowElement is not null &&
                        TryFocusLoginField(windowElement, preferredNames, excludedNames, out _))
                    {
                        SendKeys.SendWait(clearKeys);
                    }

                    SendKeys.SendWait(inputValue);
                    await Task.Delay(step.LoginFieldFallbackValueDelayMs ?? 700, cancellationToken);
                    SendKeys.SendWait("{ENTER}");
                }
            }

            if (!loginSucceeded && failIfWindowStillActive && !HasAnyWindow(mainTitles, process?.Id))
            {
                throw new InvalidOperationException($"Login did not complete for '{step.Name}': login window remained active.");
            }
        }
    }

    private static async Task ConfirmLoginCompletionAsync(
        LauncherStep step,
        Process? process,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var validationEnabled = step.LoginSuccessValidationEnabled ?? false;
        if (!validationEnabled)
        {
            return;
        }

        if (dryRun)
        {
            Log(onOutput, $"[DryRun] Would validate login completion for '{step.Name}'");
            return;
        }

        var loginTitles = DistinctTitles(
            new[] { string.IsNullOrWhiteSpace(step.LoginWindowTitle) ? step.WindowTitle : step.LoginWindowTitle },
            step.LoginFallbackWindowTitles);
        var mainTitles = DistinctTitles(
            new[] { string.IsNullOrWhiteSpace(step.LoginSuccessWindowTitle) ? step.WindowTitle : step.LoginSuccessWindowTitle },
            step.LoginSuccessFallbackWindowTitles,
            step.FallbackWindowTitles);

        var timeoutSeconds = step.LoginSuccessTimeoutSeconds ?? 45;
        var intervalMs = step.LoginSuccessIntervalMs ?? 1000;
        var requireLoginWindowClosed = step.LoginSuccessRequireLoginWindowClosed ?? true;
        var requireMainWindowVisible = step.LoginSuccessRequireMainWindowVisible ?? true;
        var requireControlMatch = step.LoginSuccessRequireControlMatch ?? step.LoginSuccessControlNames.Count > 0;

        var attempts = Math.Max(1, (int)Math.Ceiling((timeoutSeconds * 1000d) / Math.Max(1, intervalMs)));
        var lastLoginWindowActive = false;
        var lastMainWindowVisible = false;
        var lastControlsReady = false;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastLoginWindowActive = HasAnyWindow(loginTitles, process?.Id);
            lastMainWindowVisible = HasAnyWindow(mainTitles, process?.Id);
            lastControlsReady = !requireControlMatch || WindowContainsCandidateControl(mainTitles, step.LoginSuccessControlNames, process?.Id);

            var loginClosedOk = !requireLoginWindowClosed || !lastLoginWindowActive;
            var mainVisibleOk = !requireMainWindowVisible || lastMainWindowVisible;
            var controlsOk = !requireControlMatch || lastControlsReady;

            if (loginClosedOk && mainVisibleOk && controlsOk)
            {
                Log(onOutput, $"Verified login completion for '{step.Name}'");
                return;
            }

            await Task.Delay(intervalMs, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Login verification failed for '{step.Name}'. loginWindowActive={lastLoginWindowActive}, mainWindowVisible={lastMainWindowVisible}, controlsReady={lastControlsReady}");
    }

    private static async Task WaitForWindowToCloseAsync(
        LauncherStep step,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.WaitForWindowToCloseTitle))
        {
            return;
        }

        var timeoutSeconds = step.WaitForWindowToCloseTimeoutSeconds ?? 180;
        var detectionTimeoutSeconds = step.WaitForWindowToCloseDetectionTimeoutSeconds ?? timeoutSeconds;

        if (dryRun)
        {
            Log(onOutput,
                $"[DryRun] Would check for updater window '{step.WaitForWindowToCloseTitle}' (detect up to {detectionTimeoutSeconds}s, close up to {timeoutSeconds}s)");
            return;
        }

        Log(onOutput, $"Checking for updater window '{step.WaitForWindowToCloseTitle}' (up to {detectionTimeoutSeconds} second(s))");
        var sawWindow = false;
        for (var i = 0; i < detectionTimeoutSeconds; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasAnyWindow(new[] { step.WaitForWindowToCloseTitle }, null))
            {
                sawWindow = true;
                Log(onOutput, $"Updater window '{step.WaitForWindowToCloseTitle}' detected; waiting for update to complete");
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (!sawWindow)
        {
            Log(onOutput, $"Updater window '{step.WaitForWindowToCloseTitle}' not detected; no update pending, continuing");
            return;
        }

        for (var i = 0; i < timeoutSeconds; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasAnyWindow(new[] { step.WaitForWindowToCloseTitle }, null))
            {
                Log(onOutput, $"Updater window '{step.WaitForWindowToCloseTitle}' has closed; update complete");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException($"Updater window '{step.WaitForWindowToCloseTitle}' did not close within {timeoutSeconds} seconds.");
    }

    private static async Task InvokePreLoginWindowPreparationAsync(
        LauncherStep step,
        Process? process,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        await InvokeMoveWindowToMonitorAsync(step, process, beforeLogin: true, dryRun, onOutput, cancellationToken);

        if (step.MaximizeBeforeLogin ?? false)
        {
            var titles = step.MaximizeWindowTitles.Count > 0
                ? DistinctTitles(step.MaximizeWindowTitles)
                : DistinctTitles(
                    new[] { step.LoginSuccessWindowTitle, step.WindowTitle, step.LoginWindowTitle },
                    step.LoginFallbackWindowTitles,
                    step.FallbackWindowTitles);

            foreach (var title in titles)
            {
                if (dryRun)
                {
                    Log(onOutput, $"[DryRun] Would maximize window for '{step.Name}' using title '{title}'");
                }
                else if (TryFindWindow(title, process?.Id, out var handle))
                {
                    NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_MAXIMIZE);
                    Log(onOutput, $"Maximized window for '{step.Name}' using title '{title}'");
                    break;
                }
            }
        }

        if (step.PreLoginMinimizeWindowTitles.Count > 0)
        {
            var delay = step.PreLoginMinimizeDelaySeconds ?? 0;
            if (delay > 0)
            {
                if (dryRun)
                {
                    Log(onOutput, $"[DryRun] Would wait {delay} second(s) before minimizing pre-login windows for '{step.Name}'");
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
            }

            foreach (var title in DistinctTitles(step.PreLoginMinimizeWindowTitles))
            {
                if (dryRun)
                {
                    Log(onOutput, $"[DryRun] Would minimize additional window for '{step.Name}' using title '{title}'");
                }
                else if (TryFindWindow(title, null, out var handle))
                {
                    NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_MINIMIZE);
                    Log(onOutput, $"Minimized additional window for '{step.Name}' using title '{title}'");
                }
            }
        }
    }

    private static async Task InvokeMoveWindowToMonitorAsync(
        LauncherStep step,
        Process? process,
        bool beforeLogin,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var shouldMove = beforeLogin ? (step.MoveToMonitorBeforeLogin ?? false) : (step.MoveToMonitorAfterLogin ?? false);
        if (!shouldMove)
        {
            return;
        }

        var targetMonitor = string.IsNullOrWhiteSpace(step.TargetMonitor) ? "Left" : step.TargetMonitor!;
        var searchTimeoutSeconds = step.MoveWindowSearchTimeoutSeconds ?? 25;
        var searchIntervalMs = step.MoveWindowSearchIntervalMs ?? 1000;

        var windowGroups = new List<(string Label, IReadOnlyList<string> Titles)>
        {
            (step.Name, DistinctTitles(
                new[] { string.IsNullOrWhiteSpace(step.LoginSuccessWindowTitle) ? step.WindowTitle : step.LoginSuccessWindowTitle },
                step.LoginSuccessFallbackWindowTitles,
                step.FallbackWindowTitles))
        };

        if (!beforeLogin)
        {
            foreach (var spec in step.AdditionalWindowsToMoveAfterLogin)
            {
                var titles = DistinctTitles(new[] { spec.WindowTitle }, spec.FallbackWindowTitles);
                if (titles.Count > 0)
                {
                    windowGroups.Add((string.IsNullOrWhiteSpace(spec.Name) ? "Additional Window" : spec.Name, titles));
                }
            }
        }

        if (dryRun)
        {
            foreach (var group in windowGroups)
            {
                Log(onOutput, $"[DryRun] Would move '{group.Label}' to the {targetMonitor} monitor");
            }

            return;
        }

        var screens = Screen.AllScreens;
        if (screens.Length < 2)
        {
            Log(onOutput, $"Could not move '{step.Name}' because a second monitor was not detected");
            return;
        }

        var targetScreen = string.Equals(targetMonitor, "Right", StringComparison.OrdinalIgnoreCase)
            ? screens.OrderBy(s => s.WorkingArea.Left).Last()
            : screens.OrderBy(s => s.WorkingArea.Left).First();

        foreach (var group in windowGroups)
        {
            IntPtr handle = IntPtr.Zero;
            for (var attempt = 0; attempt < Math.Max(1, (int)Math.Ceiling(searchTimeoutSeconds * 1000d / Math.Max(1, searchIntervalMs))); attempt++)
            {
                handle = TryFindFirstWindow(group.Titles, process?.Id);
                if (handle != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(searchIntervalMs, cancellationToken);
            }

            if (handle == IntPtr.Zero)
            {
                Log(onOutput, $"Could not find a window to move for '{group.Label}' within {searchTimeoutSeconds} second(s)");
                continue;
            }

            NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                rect = new NativeMethods.RECT
                {
                    Left = targetScreen.WorkingArea.Left,
                    Top = targetScreen.WorkingArea.Top,
                    Right = targetScreen.WorkingArea.Left + 1200,
                    Bottom = targetScreen.WorkingArea.Top + 800
                };
            }

            var width = Math.Min(Math.Max(200, rect.Right - rect.Left), targetScreen.WorkingArea.Width);
            var height = Math.Min(Math.Max(200, rect.Bottom - rect.Top), targetScreen.WorkingArea.Height);
            NativeMethods.MoveWindow(handle, targetScreen.WorkingArea.Left, targetScreen.WorkingArea.Top, width, height, true);
            Log(onOutput, $"Moved '{group.Label}' to the {targetMonitor} monitor");
        }
    }

    private static async Task InvokeUpdateTableFlowAsync(
        LauncherStep step,
        Process? process,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var flow = step.UpdateTableFlow;
        if (flow is null)
        {
            return;
        }

        var mainWindowTitle = string.IsNullOrWhiteSpace(flow.MainWindowTitle) ? step.WindowTitle : flow.MainWindowTitle;
        var actionDelayMs = flow.ActionDelayMs ?? 1200;
        var buttonCandidates = flow.UpdateTableButtonNames.Count > 0
            ? DistinctTitles(flow.UpdateTableButtonNames)
            : DistinctTitles(new string?[] { flow.UpdateTableButtonName });

        if (buttonCandidates.Count > 0)
        {
            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would click update table button '{buttonCandidates[0]}' in window '{mainWindowTitle}'");
            }
            else
            {
                var postLoginWait = flow.PostLoginWaitSeconds ?? 0;
                if (postLoginWait > 0)
                {
                    Log(onOutput, $"Waiting {postLoginWait} second(s) for login completion");
                    await Task.Delay(TimeSpan.FromSeconds(postLoginWait), cancellationToken);
                }

                var mainWindowTitles = DistinctTitles(new[] { mainWindowTitle }, step.FallbackWindowTitles);
                if (!await TryActivateWindowAsync(mainWindowTitles, process?.Id, 30, 500, cancellationToken))
                {
                    throw new InvalidOperationException($"Could not activate '{mainWindowTitle}' before update table flow.");
                }

                var buttonTimeoutSeconds = flow.UpdateTableButtonTimeoutSeconds ?? 30;
                var clicked = false;
                foreach (var candidate in buttonCandidates)
                {
                    if (await TryClickNamedControlWithRetryAsync(mainWindowTitles, process?.Id, candidate, buttonTimeoutSeconds, 400, cancellationToken))
                    {
                        Log(onOutput, $"Clicked button '{candidate}' in '{mainWindowTitle}'");
                        clicked = true;
                        break;
                    }

                    // Some Access dialogs are not tied to the launched process handle scope.
                    if (await TryClickNamedControlWithRetryAsync(mainWindowTitles, null, candidate, 3, 250, cancellationToken))
                    {
                        Log(onOutput, $"Clicked button '{candidate}' in '{mainWindowTitle}' using desktop-wide window search");
                        clicked = true;
                        break;
                    }
                }

                if (!clicked)
                {
                    if (flow.UpdateTableFallbackKeys.Count == 0)
                    {
                        throw new InvalidOperationException("Could not click any update table button candidate.");
                    }

                    foreach (var keys in flow.UpdateTableFallbackKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                    {
                        SendKeys.SendWait(keys);
                        Log(onOutput, $"Sent update table fallback keys '{keys}'");
                        await Task.Delay(actionDelayMs, cancellationToken);
                    }
                }

                var passwordTitles = DistinctTitles(
                    new[] { flow.PasswordWindowTitle, mainWindowTitle },
                    flow.PasswordWindowTitles,
                    flow.PasswordWindowFallbackTitles,
                    step.FallbackWindowTitles);
                await TryActivateWindowAsync(passwordTitles, null, flow.PasswordWindowTimeoutSeconds ?? 45, 1000, cancellationToken);
                await Task.Delay(actionDelayMs, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(flow.Password))
        {
            var passwordWindowTitle = string.IsNullOrWhiteSpace(flow.PasswordWindowTitle) ? mainWindowTitle : flow.PasswordWindowTitle;
            var passwordEnterKeys = string.IsNullOrWhiteSpace(flow.PasswordEnterKeys) ? "{ENTER}" : flow.PasswordEnterKeys;

            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would enter update table password in '{passwordWindowTitle}'");
            }
            else
            {
                var passwordTitles = DistinctTitles(
                    new[] { flow.PasswordWindowTitle, passwordWindowTitle, mainWindowTitle },
                    flow.PasswordWindowTitles,
                    flow.PasswordWindowFallbackTitles,
                    step.FallbackWindowTitles);
                var activated = await TryActivateWindowAsync(passwordTitles, null, 5, 250, cancellationToken);
                if (!activated)
                {
                    Log(onOutput, $"Could not activate password window '{passwordWindowTitle}'; typing into current focus");
                }

                SendKeys.SendWait(flow.Password);
                await Task.Delay(actionDelayMs, cancellationToken);
                SendKeys.SendWait(passwordEnterKeys);
                Log(onOutput, "Submitted update table password");
            }
        }

        var waitAfterPasswordSeconds = flow.WaitAfterPasswordSeconds ?? 0;
        if (waitAfterPasswordSeconds > 0)
        {
            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would wait {waitAfterPasswordSeconds} seconds for update process to finish");
            }
            else
            {
                Log(onOutput, $"Waiting {waitAfterPasswordSeconds} seconds for update process to finish");
                await Task.Delay(TimeSpan.FromSeconds(waitAfterPasswordSeconds), cancellationToken);
            }
        }
    }

    private static async Task InvokeMinimizeLaunchedWindowAsync(
        LauncherStep step,
        Process? process,
        bool afterCompletion,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var shouldMinimize = afterCompletion ? (step.MinimizeAfterCompletion ?? true) : (step.MinimizeAfterLaunch ?? false);
        if (!shouldMinimize)
        {
            return;
        }

        var delaySeconds = afterCompletion ? (step.MinimizeAfterCompletionDelaySeconds ?? 0) : (step.MinimizeAfterLaunchDelaySeconds ?? 0);
        if (delaySeconds > 0)
        {
            if (dryRun)
            {
                Log(onOutput, afterCompletion
                    ? $"[DryRun] Would wait {delaySeconds} second(s) before minimizing '{step.Name}' after completion"
                    : $"[DryRun] Would wait {delaySeconds} second(s) before minimizing '{step.Name}'");
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        if (dryRun)
        {
            Log(onOutput, afterCompletion
                ? $"[DryRun] Would minimize '{step.Name}' after completion"
                : $"[DryRun] Would minimize '{step.Name}' before continuing");
            return;
        }

        var handle = process?.MainWindowHandle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            handle = TryFindFirstWindow(
                DistinctTitles(
                    step.MinimizeWindowTitles,
                    new[] { step.WindowTitle, step.Name },
                    step.FallbackWindowTitles),
                process?.Id);
        }

        if (handle == IntPtr.Zero)
        {
            Log(onOutput, $"Could not find a window handle to minimize '{step.Name}'");
            return;
        }

        if (string.Equals(ResolveWindowStyle(step), "Maximized", StringComparison.OrdinalIgnoreCase))
        {
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_MAXIMIZE);
            await Task.Delay(step.MaximizeBeforeMinimizeDelayMs ?? 300, cancellationToken);
        }

        NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_MINIMIZE);
        Log(onOutput, afterCompletion
            ? $"Minimized '{step.Name}' after completion"
            : $"Minimized '{step.Name}'");
    }

    private static async Task InvokeMinimizeAdditionalWindowTitlesAfterCompletionAsync(
        LauncherStep step,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        if (step.MinimizeAdditionalWindowTitlesAfterCompletion.Count == 0)
        {
            return;
        }

        var delaySeconds = step.MinimizeAdditionalWindowTitlesAfterCompletionDelaySeconds ?? 0;
        if (delaySeconds > 0)
        {
            if (dryRun)
            {
                Log(onOutput,
                    $"[DryRun] Would wait {delaySeconds} second(s) before minimizing additional windows after completion for '{step.Name}'");
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        foreach (var title in DistinctTitles(step.MinimizeAdditionalWindowTitlesAfterCompletion))
        {
            if (dryRun)
            {
                Log(onOutput, $"[DryRun] Would minimize additional window for '{step.Name}' using title '{title}'");
            }
            else if (TryFindWindow(title, null, out var handle))
            {
                NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_MINIMIZE);
                Log(onOutput, $"Minimized additional window for '{step.Name}' using title '{title}'");
            }
        }
    }

    private static Process? StartStepProcess(LauncherStep step, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(step.ProgramPath))
        {
            throw new InvalidOperationException($"Step '{step.Name}' is missing programPath.");
        }

        var rawProgramPath = step.ProgramPath;
        var looksLikePath = LooksLikePath(rawProgramPath);
        var resolvedProgramPath = looksLikePath
            ? ResolveAccessibleStepPath(configDirectory, step.ProgramPath, step.FallbackProgramPath)
            : step.ProgramPath;

        if (looksLikePath && string.IsNullOrWhiteSpace(resolvedProgramPath))
        {
            throw new FileNotFoundException($"Program not found for step '{step.Name}': {step.ProgramPath}");
        }

        var launchTarget = resolvedProgramPath ?? step.ProgramPath;
        var workingDirectory = ResolveWorkingDirectory(step, configDirectory, launchTarget, looksLikePath);
        var arguments = step.Arguments ?? string.Empty;

        if (Directory.Exists(launchTarget))
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = launchTarget,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                WindowStyle = ParseWindowStyle(step.WindowStyle)
            });
        }

        var extension = Path.GetExtension(launchTarget).ToLowerInvariant();
        if (extension is ".accdb" or ".accde")
        {
            var accessArguments = $"/nostartup \"{launchTarget}\"";
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                accessArguments += " " + arguments;
            }

            return Process.Start(new ProcessStartInfo
            {
                FileName = "MSACCESS.EXE",
                Arguments = accessArguments,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                WindowStyle = ParseWindowStyle(step.WindowStyle)
            });
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = launchTarget,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
            WindowStyle = ParseWindowStyle(step.WindowStyle)
        });
    }

    private static string GetDryRunTarget(LauncherStep step, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(step.ProgramPath))
        {
            return string.Empty;
        }

        if (!LooksLikePath(step.ProgramPath))
        {
            return step.ProgramPath;
        }

        return ResolveAccessibleStepPath(configDirectory, step.ProgramPath, step.FallbackProgramPath) ?? ResolveStepPath(configDirectory, step.ProgramPath);
    }

    private static List<int> GetTrackedProcessIds(LauncherStep step)
    {
        var names = step.CloseProcessNames.Count > 0
            ? step.CloseProcessNames
            : step.RunningProcessNames.Count > 0
                    ? step.RunningProcessNames
                : BuildProcessCandidates(step);

        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(name =>
            {
                try
                {
                    return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));
                }
                catch
                {
                    return Array.Empty<Process>();
                }
            })
            .Select(p => p.Id)
            .Distinct()
            .ToList();
    }

    private static List<string> BuildProcessCandidates(LauncherStep step)
    {
        var processNames = new List<string>(step.RunningProcessNames);
        if (!string.IsNullOrWhiteSpace(step.ProgramPath))
        {
            var leaf = Path.GetFileNameWithoutExtension(step.ProgramPath);
            if (!string.IsNullOrWhiteSpace(leaf))
            {
                processNames.Add(leaf);
            }

            var extension = Path.GetExtension(step.ProgramPath).ToLowerInvariant();
            if (extension is ".accdb" or ".accde")
            {
                processNames.Add("MSACCESS");
            }
        }

        return processNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveWorkingDirectory(LauncherStep step, string configDirectory, string launchTarget, bool looksLikePath)
    {
        if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
        {
            return ResolveStepPath(configDirectory, step.WorkingDirectory);
        }

        if (looksLikePath)
        {
            if (Directory.Exists(launchTarget))
            {
                return launchTarget;
            }

            var parent = Path.GetDirectoryName(launchTarget);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return parent;
            }
        }

        return configDirectory;
    }

    private static string ResolveStepPath(string configDirectory, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(configDirectory, path));
    }

    private static string? ResolveAccessibleStepPath(string configDirectory, string? primaryPath, string? fallbackPath)
    {
        foreach (var candidate in new[] { primaryPath, fallbackPath })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var resolved = ResolveStepPath(configDirectory, candidate);
            if (File.Exists(resolved) || Directory.Exists(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool LooksLikePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains('/') || path.StartsWith(".", StringComparison.Ordinal));
    }

    private static string ResolveWindowStyle(LauncherStep step)
    {
        return string.IsNullOrWhiteSpace(step.WindowStyle) ? "Normal" : step.WindowStyle!;
    }

    private static ProcessWindowStyle ParseWindowStyle(string? windowStyle)
    {
        return windowStyle?.Trim().ToLowerInvariant() switch
        {
            "maximized" => ProcessWindowStyle.Maximized,
            "minimized" => ProcessWindowStyle.Minimized,
            "hidden" => ProcessWindowStyle.Hidden,
            _ => ProcessWindowStyle.Normal
        };
    }

    private static async Task<bool> TryActivateWindowAsync(
        IReadOnlyList<string> titles,
        int? processId,
        int timeoutSeconds,
        int intervalMs,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, (int)Math.Ceiling(timeoutSeconds * 1000d / Math.Max(1, intervalMs)));
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = TryFindFirstWindow(titles, processId);
            if (handle != IntPtr.Zero)
            {
                NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(handle);
                return true;
            }

            await Task.Delay(intervalMs, cancellationToken);
        }

        return false;
    }

    private static bool HasAnyWindow(IEnumerable<string> titles, int? processId)
    {
        return TryFindFirstWindow(DistinctTitles(titles), processId) != IntPtr.Zero;
    }

    private static bool WindowContainsCandidateControl(IEnumerable<string> titles, IEnumerable<string> candidateNames, int? processId)
    {
        var handle = TryFindFirstWindow(DistinctTitles(titles), processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var window = AutomationElement.FromHandle(handle);
        return FindControlByCandidateName(window, candidateNames) is not null;
    }

    private static bool TryClickNamedControl(string? primaryTitle, IReadOnlyList<string> titles, int? processId, string candidateName)
    {
        var handle = TryFindFirstWindow(titles, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var window = AutomationElement.FromHandle(handle);
        var control = FindControlByCandidateName(window, new[] { candidateName });
        if (control is null)
        {
            return false;
        }

        if (TryInvokeControlOrAncestor(control))
        {
            return true;
        }

        return ClickControlCenter(control);
    }

    private static async Task<bool> TryClickNamedControlWithRetryAsync(
        IReadOnlyList<string> titles,
        int? processId,
        string candidateName,
        int timeoutSeconds,
        int intervalMs,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, (int)Math.Ceiling(timeoutSeconds * 1000d / Math.Max(1, intervalMs)));
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryClickNamedControl(null, titles, processId, candidateName))
            {
                return true;
            }

            await Task.Delay(intervalMs, cancellationToken);
        }

        return false;
    }

    private static bool TryInvokeControlOrAncestor(AutomationElement control)
    {
        AutomationElement? current = control;
        while (current is not null)
        {
            if (current.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObj) &&
                invokePatternObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return true;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return false;
    }

    private static bool TryGetAutomationElement(IntPtr handle, out AutomationElement? element)
    {
        element = null;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            element = AutomationElement.FromHandle(handle);
            return element is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFocusLoginField(AutomationElement window, IReadOnlyList<string> preferredNames, IReadOnlyList<string> excludedNames, out AutomationElement? focusedControl)
    {
        focusedControl = null;
        if (window is null)
        {
            return false;
        }

        foreach (AutomationElement control in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (control.Current.ControlType != ControlType.Edit)
            {
                continue;
            }

            var controlName = control.Current.Name;
            var automationId = control.Current.AutomationId;
            var matchesPreferred = preferredNames.Any(candidate => NameMatchesCandidate(controlName, candidate) || NameMatchesCandidate(automationId, candidate));
            var matchesExcluded = excludedNames.Any(candidate => NameMatchesCandidate(controlName, candidate) || NameMatchesCandidate(automationId, candidate));
            if ((preferredNames.Count == 0 || matchesPreferred) && !matchesExcluded)
            {
                try
                {
                    control.SetFocus();
                    focusedControl = control;
                    return true;
                }
                catch
                {
                    continue;
                }
            }
        }

        foreach (AutomationElement control in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (control.Current.ControlType != ControlType.Edit)
            {
                continue;
            }

            try
            {
                control.SetFocus();
                focusedControl = control;
                return true;
            }
            catch
            {
                continue;
            }
        }

        return false;
    }

    private static bool NameMatchesCandidate(string? value, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedValue = NormalizeName(value);
        var normalizedCandidate = NormalizeName(candidate);
        return !string.IsNullOrWhiteSpace(normalizedValue) &&
               !string.IsNullOrWhiteSpace(normalizedCandidate) &&
               normalizedValue.Contains(normalizedCandidate, StringComparison.Ordinal);
    }

    private static string? TryGetFirstTextLoginValue(IEnumerable<LauncherKeySequenceEntry> entries)
    {
        foreach (var entry in entries)
        {
            var keys = entry.Keys ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keys))
            {
                continue;
            }

            if (IsSimpleTextInput(keys))
            {
                return keys;
            }
        }

        return null;
    }

    private static bool IsSimpleTextInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.');
    }

    private static AutomationElement? FindControlByCandidateName(AutomationElement window, IEnumerable<string> candidateNames)
    {
        var candidates = candidateNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(NormalizeName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var controls = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement control in controls)
        {
            var controlType = control.Current.ControlType;
            if (controlType != ControlType.Button &&
                controlType != ControlType.Hyperlink &&
                controlType != ControlType.MenuItem &&
                controlType != ControlType.Custom &&
                controlType != ControlType.Text)
            {
                continue;
            }

            var normalizedName = NormalizeName(control.Current.Name);
            var normalizedAutomationId = NormalizeName(control.Current.AutomationId);
            if (string.IsNullOrWhiteSpace(normalizedName) && string.IsNullOrWhiteSpace(normalizedAutomationId))
            {
                continue;
            }

            if (candidates.Any(candidate =>
                    (!string.IsNullOrWhiteSpace(normalizedName) && normalizedName.Contains(candidate, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(normalizedAutomationId) && normalizedAutomationId.Contains(candidate, StringComparison.Ordinal))))
            {
                return control;
            }
        }

        return null;
    }

    private static bool ClickControlCenter(AutomationElement control)
    {
        var rect = control.Current.BoundingRectangle;
        if (rect.IsEmpty)
        {
            return false;
        }

        var x = (int)((rect.Left + rect.Right) / 2);
        var y = (int)((rect.Top + rect.Bottom) / 2);
        NativeMethods.SetCursorPos(x, y);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        return true;
    }

    private static IntPtr TryFindFirstWindow(IReadOnlyList<string> titles, int? processId)
    {
        var found = TryFindFirstWindowCore(titles, processId);
        if (found != IntPtr.Zero || !processId.HasValue || processId.Value <= 0)
        {
            return found;
        }

        return TryFindFirstWindowCore(titles, null);
    }

    private static IntPtr TryFindFirstWindowCore(IReadOnlyList<string> titles, int? processId)
    {
        foreach (var window in EnumerateWindows())
        {
            if (processId.HasValue && processId.Value > 0 && window.ProcessId != processId.Value)
            {
                continue;
            }

            if (titles.Any(title => TitleEqualsOrContains(window.Title, title)))
            {
                return window.Handle;
            }
        }

        return IntPtr.Zero;
    }

    private static bool TryFindWindow(string title, int? processId, out IntPtr handle)
    {
        handle = TryFindFirstWindow(new[] { title }, processId);
        return handle != IntPtr.Zero;
    }

    private static IEnumerable<WindowInfo> EnumerateWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var builder = new StringBuilder(512);
            _ = NativeMethods.GetWindowText(hWnd, builder, builder.Capacity);
            var title = builder.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            windows.Add(new WindowInfo(hWnd, title, (int)pid));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool TitleEqualsOrContains(string title, string candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate) &&
               (title.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                title.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> DistinctTitles(params IEnumerable<string?>[] titleSets)
    {
        return titleSets
            .SelectMany(set => set)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SaveLauncherSessionState(string configPath, IReadOnlyCollection<LauncherSessionStep> launchedSteps)
    {
        var sessionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher");
        Directory.CreateDirectory(sessionDirectory);

        var sessionState = new LauncherSessionState
        {
            SchemaVersion = 1,
            Mode = "Start",
            ConfigPath = configPath,
            RecordedAt = DateTimeOffset.Now.ToString("O"),
            LaunchedSteps = launchedSteps.ToList()
        };

        var sessionFile = Path.Combine(sessionDirectory, "launcher-session.json");
        File.WriteAllText(sessionFile, JsonSerializer.Serialize(sessionState, new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void Log(Action<string>? onOutput, string message)
    {
        onOutput?.Invoke(message);
    }

    private static void LogStepHeader(Action<string>? onOutput, string stepName)
    {
        onOutput?.Invoke(string.Empty);
        onOutput?.Invoke($">  {stepName}");
        onOutput?.Invoke(new string('-', 84));
    }

    private sealed record WindowInfo(IntPtr Handle, string Title, int ProcessId);

    private sealed class LauncherSessionState
    {
        public int SchemaVersion { get; set; }

        public string Mode { get; set; } = "Start";

        public string ConfigPath { get; set; } = string.Empty;

        public string RecordedAt { get; set; } = string.Empty;

        public List<LauncherSessionStep> LaunchedSteps { get; set; } = new();
    }

    private sealed class LauncherSessionStep
    {
        public string StepName { get; set; } = string.Empty;

        public string StepType { get; set; } = string.Empty;

        public List<int> ProcessIds { get; set; } = new();

        public bool Closed { get; set; }

        public string LaunchedAt { get; set; } = string.Empty;
    }

    private static class NativeMethods
    {
        internal const int SW_RESTORE = 9;
        internal const int SW_MAXIMIZE = 3;
        internal const int SW_MINIMIZE = 6;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    }
}