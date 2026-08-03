using System.IO;
using System.Text.Json;

namespace Launcher.Core.Services;

public sealed class LauncherLearningService
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _stateFilePath;

    public LauncherLearningService(string? stateFilePath = null)
    {
        _stateFilePath = string.IsNullOrWhiteSpace(stateFilePath)
            ? Path.Combine(GetLauncherDataDirectory(), "launcher-learning.json")
            : stateFilePath;
    }

    public string StateFilePath => _stateFilePath;

    public bool ResetHistory()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RecordRun(IEnumerable<string> observedStepNames)
    {
        var orderedSteps = DistinctOrdered(observedStepNames).ToList();
        if (orderedSteps.Count == 0)
        {
            return;
        }

        var state = LoadState();
        state.SchemaVersion = CurrentSchemaVersion;
        state.TotalRuns++;
        state.UpdatedAt = DateTimeOffset.Now.ToString("O");

        var first = orderedSteps[0];
        IncrementCounter(state.FirstStepCounts, first);

        for (var i = 0; i < orderedSteps.Count; i++)
        {
            var stepName = orderedSteps[i];
            if (!state.StepStats.TryGetValue(stepName, out var stats))
            {
                stats = new LauncherLearningStepStats();
                state.StepStats[stepName] = stats;
            }

            stats.LaunchCount++;
            stats.PositionSum += i + 1;
            stats.LastObservedAt = state.UpdatedAt;

            if (i + 1 >= orderedSteps.Count)
            {
                continue;
            }

            var nextStep = orderedSteps[i + 1];
            if (!state.TransitionCounts.TryGetValue(stepName, out var nextCounts))
            {
                nextCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                state.TransitionCounts[stepName] = nextCounts;
            }

            IncrementCounter(nextCounts, nextStep);
        }

        SaveState(state);
    }

    public IReadOnlyList<string> GetRecommendedOrder(IEnumerable<string> candidateStepNames, int minRunsBeforeSuggestions)
    {
        var candidates = DistinctOrdered(candidateStepNames).ToList();
        if (candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var state = LoadState();
        var requiredRuns = Math.Max(1, minRunsBeforeSuggestions);
        if (state.TotalRuns < requiredRuns)
        {
            return Array.Empty<string>();
        }

        var originalIndexByName = candidates
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        var remaining = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
        var recommended = new List<string>(candidates.Count);

        var current = remaining
            .OrderByDescending(name => GetCount(state.FirstStepCounts, name))
            .ThenByDescending(name => GetLaunchCount(state, name))
            .ThenBy(name => originalIndexByName[name])
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(current))
        {
            return Array.Empty<string>();
        }

        recommended.Add(current);
        remaining.Remove(current);

        while (remaining.Count > 0)
        {
            var next = SelectNextStep(state, current, remaining, originalIndexByName);
            if (string.IsNullOrWhiteSpace(next))
            {
                break;
            }

            recommended.Add(next);
            remaining.Remove(next);
            current = next;
        }

        foreach (var stepName in candidates)
        {
            if (!recommended.Contains(stepName, StringComparer.OrdinalIgnoreCase))
            {
                recommended.Add(stepName);
            }
        }

        return recommended;
    }

    private static string GetLauncherDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private LauncherLearningState LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new LauncherLearningState();
            }

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<LauncherLearningState>(json);
            return state ?? new LauncherLearningState();
        }
        catch
        {
            return new LauncherLearningState();
        }
    }

    private void SaveState(LauncherLearningState state)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stateFilePath, json);
    }

    private static string? SelectNextStep(
        LauncherLearningState state,
        string current,
        IReadOnlyCollection<string> remaining,
        IReadOnlyDictionary<string, int> originalIndexByName)
    {
        if (state.TransitionCounts.TryGetValue(current, out var nextCounts))
        {
            var transitionCandidate = remaining
                .OrderByDescending(name => GetCount(nextCounts, name))
                .ThenByDescending(name => GetLaunchCount(state, name))
                .ThenBy(name => originalIndexByName[name])
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(transitionCandidate))
            {
                var count = GetCount(nextCounts, transitionCandidate);
                if (count > 0)
                {
                    return transitionCandidate;
                }
            }
        }

        return remaining
            .OrderByDescending(name => GetLaunchCount(state, name))
            .ThenBy(name => originalIndexByName[name])
            .FirstOrDefault();
    }

    private static IEnumerable<string> DistinctOrdered(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawName in names)
        {
            var name = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }

    private static int GetCount(IReadOnlyDictionary<string, int> source, string key)
    {
        return source.TryGetValue(key, out var count) ? count : 0;
    }

    private static int GetLaunchCount(LauncherLearningState state, string stepName)
    {
        return state.StepStats.TryGetValue(stepName, out var stats) ? stats.LaunchCount : 0;
    }

    private static void IncrementCounter(IDictionary<string, int> source, string key)
    {
        if (!source.TryGetValue(key, out var current))
        {
            current = 0;
        }

        source[key] = current + 1;
    }

    private sealed class LauncherLearningState
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public int TotalRuns { get; set; }

        public string UpdatedAt { get; set; } = string.Empty;

        public Dictionary<string, LauncherLearningStepStats> StepStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> FirstStepCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, int>> TransitionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LauncherLearningStepStats
    {
        public int LaunchCount { get; set; }

        public int PositionSum { get; set; }

        public string LastObservedAt { get; set; } = string.Empty;
    }
}
