using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;

namespace Matarchive.Web.Pages;

public sealed class DashboardModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public DashboardModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    public int TaskCount { get; private set; }
    public int EnabledTaskCount { get; private set; }
    public int ConnectionCount { get; private set; }
    public int UserCount { get; private set; }
    public int ApiKeyCount { get; private set; }
    public int QueuedRunCount { get; private set; }
    public IReadOnlyList<DashboardTaskViewModel> Tasks { get; private set; } = [];
    public IReadOnlyList<DashboardRunViewModel> RecentRuns { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var tasks = await _repository.GetTasksAsync();
        var connections = await _repository.GetConnectionsAsync();
        var users = await _repository.GetUsersAsync();
        var apiKeys = await _repository.GetApiKeysAsync();
        var runs = await _repository.GetRunsAsync();

        TaskCount = tasks.Count;
        EnabledTaskCount = tasks.Count(task => task.Enabled);
        ConnectionCount = connections.Count;
        UserCount = users.Count;
        ApiKeyCount = apiKeys.Count;
        QueuedRunCount = runs.Count(run => run.Status is "Queued" or "Running");

        var connectionLookup = connections.ToDictionary(connection => connection.Id, connection => connection);
        Tasks = tasks
            .OrderByDescending(task => task.UpdatedAt)
            .Take(6)
            .Select(task =>
            {
                connectionLookup.TryGetValue(task.SourceConnectionId, out var source);
                connectionLookup.TryGetValue(task.DestinationConnectionId, out var destination);
                return new DashboardTaskViewModel(
                    task.Name,
                    task.TaskType,
                    source?.Name ?? "Unknown",
                    destination?.Name ?? "Unknown",
                    task.RunEveryMinutes,
                    task.LastRunAt,
                    ResolveStatusClass(task.LastStatus),
                    task.LastStatus);
            })
            .ToList();

        var taskLookup = tasks.ToDictionary(task => task.Id, task => task.Name);
        RecentRuns = runs
            .OrderByDescending(run => run.QueuedAt)
            .Take(6)
            .Select(run =>
            {
                taskLookup.TryGetValue(run.TaskId, out var taskName);
                return new DashboardRunViewModel(
                    taskName ?? "Unbekannter Task",
                    run.Trigger,
                    run.Status,
                    run.Message,
                    run.QueuedAt,
                    ResolveStatusClass(run.Status));
            })
            .ToList();
    }

    private static string ResolveStatusClass(string status)
    {
        return status switch
        {
            "Succeeded" => "pill-success",
            "Failed" => "pill-danger",
            "Running" => "pill-warning",
            "Queued" => "pill-info",
            _ => "pill"
        };
    }
}

public sealed record DashboardTaskViewModel(
    string Name,
    string TaskType,
    string SourceName,
    string DestinationName,
    int? RunEveryMinutes,
    DateTimeOffset? LastRunAt,
    string StatusClass,
    string Status);

public sealed record DashboardRunViewModel(
    string TaskName,
    string Trigger,
    string Status,
    string Message,
    DateTimeOffset QueuedAt,
    string StatusClass);

