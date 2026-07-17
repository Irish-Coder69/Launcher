using System.Diagnostics;

namespace Launcher.Core.Services;

public sealed class LauncherScriptBridge
{
    public async Task<int> RunAsync(
        string launcherScriptPath,
        string configPath,
        LauncherMode mode,
        bool dryRun,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default)
    {
        var hostExecutable = ResolvePowerShellHost();
        var modeValue = mode.ToString();

        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File \"{launcherScriptPath}\" -ConfigPath \"{configPath}\" -Mode {modeValue}";
        if (dryRun)
        {
            arguments += " -DryRun";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostExecutable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(launcherScriptPath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke("[stderr] " + args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start launcher process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public void LaunchInteractiveStartAndWait(string launcherScriptPath, string configPath, bool dryRun)
    {
        var hostExecutable = ResolvePowerShellHost();

        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File \"{launcherScriptPath}\" -ConfigPath \"{configPath}\" -Mode StartAndWaitForCloseCommand";
        if (dryRun)
        {
            arguments += " -DryRun";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostExecutable,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherScriptPath) ?? Environment.CurrentDirectory
        };

        Process.Start(startInfo);
    }

    private static string ResolvePowerShellHost()
    {
        var pwshPath = FindExecutableInPath("pwsh.exe");
        if (!string.IsNullOrWhiteSpace(pwshPath))
        {
            return pwshPath;
        }

        var powershellPath = FindExecutableInPath("powershell.exe");
        if (!string.IsNullOrWhiteSpace(powershellPath))
        {
            return powershellPath;
        }

        throw new FileNotFoundException("PowerShell host was not found. Install PowerShell 7 or Windows PowerShell.");
    }

    private static string? FindExecutableInPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var pathPart in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(pathPart))
            {
                continue;
            }

            var candidate = Path.Combine(pathPart.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
