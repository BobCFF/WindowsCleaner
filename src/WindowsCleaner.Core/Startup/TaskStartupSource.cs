using Microsoft.Win32.TaskScheduler;

namespace WindowsCleaner.Core;

/// <summary>Non-Microsoft scheduled tasks that run at user logon. Not unit-tested (needs the real Task Scheduler).</summary>
public sealed class TaskStartupSource : IStartupSource
{
    public const string SourceName = "Scheduled task";

    public string Name => SourceName;

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        using var ts = new TaskService();
        foreach (var t in ts.AllTasks)
        {
            try
            {
                if (t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!t.Definition.Triggers.Any(x => x.TriggerType == TaskTriggerType.Logon)) continue;
                var command = string.Join("; ", t.Definition.Actions.Select(a => a.ToString()));
                result.Add(new StartupEntry(t.Path, t.Name, command, SourceName, t.Enabled));
            }
            catch (Exception) { /* unreadable task: skip */ }
        }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        using var ts = new TaskService();
        var task = ts.GetTask(entry.Id) ?? throw new InvalidOperationException($"Task not found: {entry.Id}");
        task.Enabled = enabled;
    }
}
