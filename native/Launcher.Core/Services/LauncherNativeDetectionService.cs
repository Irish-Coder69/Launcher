using System.Diagnostics;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LauncherNativeDetectionService
{
    public bool IsStepRunning(LauncherStep step)
    {
        if (!string.Equals(step.Type, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var titles = BuildWindowTitleCandidates(step);
        var processNames = BuildProcessCandidates(step);

        var windowMatch = titles.Count > 0 && WindowTitleMatches(titles);
        var processMatch = processNames.Count > 0 && ProcessMatches(processNames);

        if (titles.Count > 0 && processNames.Count > 0)
        {
            return windowMatch && processMatch;
        }

        if (titles.Count > 0)
        {
            return windowMatch;
        }

        if (processNames.Count > 0)
        {
            return processMatch;
        }

        return false;
    }

    public IReadOnlyList<Process> FindCloseTargets(LauncherStep step)
    {
        var closeTitles = BuildCloseWindowTitleCandidates(step);
        var closeProcesses = BuildCloseProcessCandidates(step);

        var matches = new List<Process>();

        if (closeProcesses.Count > 0)
        {
            foreach (var processName in closeProcesses)
            {
                matches.AddRange(Process.GetProcessesByName(processName));
            }
        }

        if (matches.Count == 0 && closeTitles.Count > 0)
        {
            foreach (var process in GetWindowedProcesses())
            {
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                if (closeTitles.Any(candidate => TitleEqualsOrContains(title, candidate)))
                {
                    matches.Add(process);
                }
            }
        }

        return matches
            .Where(p => !string.Equals(p.ProcessName, "Idle", StringComparison.OrdinalIgnoreCase))
             .GroupBy(p => p.Id)
             .Select(g => g.First())
             .ToList();
    }

    private static List<string> BuildWindowTitleCandidates(LauncherStep step)
    {
        var titles = new List<string>();
        titles.AddRange(step.RunningWindowTitles);

        if (!string.IsNullOrWhiteSpace(step.WindowTitle))
        {
            titles.Add(step.WindowTitle);
        }

        titles.AddRange(step.FallbackWindowTitles);

        return titles
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildProcessCandidates(LauncherStep step)
    {
        var processNames = new List<string>();
        processNames.AddRange(step.RunningProcessNames);

        if (!string.IsNullOrWhiteSpace(step.ProgramPath))
        {
            var leaf = Path.GetFileNameWithoutExtension(step.ProgramPath);
            if (!string.IsNullOrWhiteSpace(leaf))
            {
                processNames.Add(leaf);
            }

            var extension = Path.GetExtension(step.ProgramPath)?.ToLowerInvariant();
            if (extension is ".accdb" or ".accde")
            {
                processNames.Add("MSACCESS");
            }
        }

        return processNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildCloseWindowTitleCandidates(LauncherStep step)
    {
        var titles = new List<string>();
        titles.AddRange(step.CloseWindowTitles);

        if (titles.Count == 0)
        {
            titles.AddRange(BuildWindowTitleCandidates(step));
        }

        return titles
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildCloseProcessCandidates(LauncherStep step)
    {
        var processNames = new List<string>();
        processNames.AddRange(step.CloseProcessNames);

        if (processNames.Count == 0)
        {
            processNames.AddRange(BuildProcessCandidates(step));
        }

        return processNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool WindowTitleMatches(IReadOnlyCollection<string> titleCandidates)
    {
        var windows = GetWindowedProcesses();
        foreach (var process in windows)
        {
            var title = process.MainWindowTitle;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (titleCandidates.Any(candidate => TitleEqualsOrContains(title, candidate)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ProcessMatches(IReadOnlyCollection<string> processCandidates)
    {
        foreach (var candidate in processCandidates)
        {
            var processName = Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            var running = Process.GetProcessesByName(processName);
            if (running.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Process> GetWindowedProcesses()
    {
        return Process.GetProcesses().Where(p =>
        {
            try
            {
                return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle);
            }
            catch
            {
                return false;
            }
        });
    }

    private static bool TitleEqualsOrContains(string title, string candidate)
    {
        return title.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
               title.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
