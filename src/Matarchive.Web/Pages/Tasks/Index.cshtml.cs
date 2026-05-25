using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Matarchive.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Tasks;

public sealed class IndexModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;
    private readonly TaskExecutionQueue _queue;

    public IndexModel(MatarchiveRepository repository, TaskExecutionQueue queue)
    {
        _repository = repository;
        _queue = queue;
    }

    public IReadOnlyList<TaskListItemViewModel> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var tasks = await _repository.GetTasksAsync();
        var connections = await _repository.GetConnectionsAsync();

        var connectionLookup = connections.ToDictionary(connection => connection.Id, connection => connection);
        Items = tasks
            .OrderByDescending(task => task.UpdatedAt)
            .Select(task =>
            {
                connectionLookup.TryGetValue(task.SourceConnectionId, out var source);
                connectionLookup.TryGetValue(task.DestinationConnectionId, out var destination);
                return new TaskListItemViewModel(
                    task.Id,
                    task.Name,
                    task.TaskType,
                    TaskOptionCatalog.NormalizeArchiveFormat(task.ArchiveFormat, task.CompressToZip),
                    TaskOptionCatalog.NormalizeCompressionLevel(task.CompressionLevel),
                    TaskOptionCatalog.NormalizeTransferMode(task.TransferMode),
                    TaskSchedulePolicy.FormatSummary(task),
                    TaskRetentionPolicy.FormatSummary(task),
                    source?.Name ?? "Unknown",
                    destination?.Name ?? "Unknown",
                    source is null ? "-" : ConnectionTypeCatalog.GetDescriptor(source.Type).DisplayName,
                    destination is null ? "-" : ConnectionTypeCatalog.GetDescriptor(destination.Type).DisplayName,
                    task.Enabled,
                    task.RunEveryMinutes,
                    task.LastRunAt,
                    task.LastMessage,
                    ResolveStatusClass(task.LastStatus),
                    task.LastStatus);
            })
            .ToList();
    }

    public async Task<IActionResult> OnPostRunAsync(Guid id)
    {
        var task = await _repository.GetTaskByIdAsync(id);
        if (task is null)
        {
            FlashError = "Der Task wurde nicht gefunden.";
            return RedirectToPage();
        }

        var run = new TaskRun
        {
            TaskId = task.Id,
            Trigger = "Manual",
            Status = "Queued",
            QueuedAt = DateTimeOffset.UtcNow
        };
        await _repository.SaveRunAsync(run);
        await _queue.EnqueueAsync(new TaskRunRequest(task.Id, run.Id, "Manual"));

        FlashSuccess = $"Task {task.Name} wurde zur Ausführung eingeplant.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var task = await _repository.GetTaskByIdAsync(id);
        if (task is null)
        {
            FlashError = "Der Task wurde nicht gefunden.";
            return RedirectToPage();
        }

        await _repository.DeleteRunsForTaskAsync(id);
        await _repository.DeleteTaskAsync(id);

        FlashSuccess = $"Task {task.Name} wurde gelöscht.";
        return RedirectToPage();
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

public sealed record TaskListItemViewModel(
    Guid Id,
    string Name,
    string TaskType,
    string ArchiveFormat,
    string CompressionLevel,
    string TransferMode,
    string ScheduleSummary,
    string RetentionSummary,
    string SourceName,
    string DestinationName,
    string SourceType,
    string DestinationType,
    bool Enabled,
    int? RunEveryMinutes,
    DateTimeOffset? LastRunAt,
    string LastMessage,
    string StatusClass,
    string Status);
