using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Matarchive.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Tasks;

public sealed class DetailsModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;
    private readonly TaskExecutionQueue _queue;

    public DetailsModel(MatarchiveRepository repository, TaskExecutionQueue queue)
    {
        _repository = repository;
        _queue = queue;
    }

    public TaskDetailsViewModel Task { get; private set; } = default!;
    public IReadOnlyList<TaskRunViewModel> Runs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        return await LoadAsync(id);
    }

    public async Task<IActionResult> OnPostRunAsync(Guid id)
    {
        var task = await _repository.GetTaskByIdAsync(id);
        if (task is null)
        {
            FlashError = "Der Task wurde nicht gefunden.";
            return RedirectToPage("/Tasks/Index");
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

        FlashSuccess = $"Task {task.Name} wurde gestartet.";
        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> LoadAsync(Guid id)
    {
        var task = await _repository.GetTaskByIdAsync(id);
        if (task is null)
        {
            return NotFound();
        }

        var connections = await _repository.GetConnectionsAsync();
        var runs = await _repository.GetRunsForTaskAsync(id);

        var source = connections.FirstOrDefault(connection => connection.Id == task.SourceConnectionId);
        var destination = connections.FirstOrDefault(connection => connection.Id == task.DestinationConnectionId);

        Task = new TaskDetailsViewModel(
            task.Id,
            task.Name,
            task.TaskType,
            TaskOptionCatalog.NormalizeArchiveFormat(task.ArchiveFormat, task.CompressToZip),
            TaskOptionCatalog.NormalizeCompressionLevel(task.CompressionLevel),
            TaskOptionCatalog.NormalizeTransferMode(task.TransferMode),
            task.VerifyDestination,
            task.KeepLocalStagingOnFailure,
            TaskSchedulePolicy.FormatSummary(task),
            TaskRetentionPolicy.FormatSummary(task),
            task.ArchiveFileNamePattern,
            source?.Name ?? "Unknown",
            destination?.Name ?? "Unknown",
            task.Enabled,
            task.RunEveryMinutes,
            task.LastStatus,
            task.LastRunAt,
            task.LastMessage);

        Runs = runs
            .OrderByDescending(run => run.QueuedAt)
            .Take(12)
            .Select(run => new TaskRunViewModel(
                run.Id,
                run.Status,
                run.Trigger,
                run.Message,
                run.QueuedAt,
                ResolveStatusClass(run.Status)))
            .ToList();

        return Page();
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

public sealed record TaskDetailsViewModel(
    Guid Id,
    string Name,
    string TaskType,
    string ArchiveFormat,
    string CompressionLevel,
    string TransferMode,
    bool VerifyDestination,
    bool KeepLocalStagingOnFailure,
    string ScheduleSummary,
    string RetentionSummary,
    string ArchiveFileNamePattern,
    string SourceName,
    string DestinationName,
    bool Enabled,
    int? RunEveryMinutes,
    string LastStatus,
    DateTimeOffset? LastRunAt,
    string LastMessage);

public sealed record TaskRunViewModel(
    Guid Id,
    string Status,
    string Trigger,
    string Message,
    DateTimeOffset QueuedAt,
    string StatusClass);
