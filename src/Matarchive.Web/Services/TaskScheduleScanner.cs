using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;

namespace Matarchive.Web.Services;

public sealed class TaskScheduleScanner : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly MatarchiveRepository _repository;
    private readonly TaskExecutionQueue _queue;
    private readonly ILogger<TaskScheduleScanner> _logger;

    public TaskScheduleScanner(
        MatarchiveRepository repository,
        TaskExecutionQueue queue,
        ILogger<TaskScheduleScanner> logger)
    {
        _repository = repository;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CheckDueTasksAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckDueTasksAsync(stoppingToken);
        }
    }

    private async Task CheckDueTasksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tasks = await _repository.GetTasksAsync(cancellationToken);
            var runs = await _repository.GetRunsAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;

            foreach (var task in tasks.Where(task => task.Enabled))
            {
                var hasPending = runs.Any(run => run.TaskId == task.Id && (run.Status == "Queued" || run.Status == "Running"));
                if (hasPending)
                {
                    continue;
                }

                var dueOccurrence = TaskSchedulePolicy.GetDueOccurrence(task, runs, now);
                if (dueOccurrence is null)
                {
                    continue;
                }

                var run = new TaskRun
                {
                    TaskId = task.Id,
                    Trigger = "Scheduler",
                    Status = "Queued",
                    QueuedAt = now,
                    Message = $"Scheduled occurrence: {dueOccurrence.Value:O}"
                };
                await _repository.SaveRunAsync(run, cancellationToken);
                await _queue.EnqueueAsync(new TaskRunRequest(task.Id, run.Id, "Scheduler"), cancellationToken);
                _logger.LogInformation("Queued scheduled run for task {TaskId}", task.Id);
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _logger.LogError(ex, "Failed to scan scheduled tasks");
        }
    }
}
