using System.IO.Compression;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Matarchive.Web.Services;

public sealed class TaskRunnerWorker : BackgroundService
{
    private readonly TaskExecutionQueue _queue;
    private readonly MatarchiveRepository _repository;
    private readonly NotificationService _notificationService;
    private readonly SmbClientTransferService _smbTransferService;
    private readonly IHostEnvironment _environment;
    private readonly MatarchiveOptions _options;
    private readonly ILogger<TaskRunnerWorker> _logger;

    public TaskRunnerWorker(
        TaskExecutionQueue queue,
        MatarchiveRepository repository,
        NotificationService notificationService,
        SmbClientTransferService smbTransferService,
        IOptions<MatarchiveOptions> options,
        IHostEnvironment environment,
        ILogger<TaskRunnerWorker> logger)
    {
        _queue = queue;
        _repository = repository;
        _notificationService = notificationService;
        _smbTransferService = smbTransferService;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ExecuteTaskAsync(request, stoppingToken);
        }
    }

    public async Task QueueManualRunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var run = new TaskRun
        {
            TaskId = taskId,
            Trigger = "Manual",
            Status = "Queued",
            QueuedAt = DateTimeOffset.UtcNow
        };

        await _repository.SaveRunAsync(run, cancellationToken);
        await _queue.EnqueueAsync(new TaskRunRequest(taskId, run.Id, "Manual"), cancellationToken);
    }

    private async Task ExecuteTaskAsync(TaskRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _repository.GetRunByIdAsync(request.RunId, cancellationToken);
            if (run is null)
            {
                return;
            }

            var task = await _repository.GetTaskByIdAsync(request.TaskId, cancellationToken);
            if (task is null)
            {
                run.Status = "Failed";
                run.Message = "Task could not be found.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await _repository.SaveRunAsync(run, cancellationToken);
                return;
            }

            var source = await _repository.GetConnectionByIdAsync(task.SourceConnectionId, cancellationToken);
            var destination = await _repository.GetConnectionByIdAsync(task.DestinationConnectionId, cancellationToken);

            if (source is null || destination is null)
            {
                throw new InvalidOperationException("Source or destination connection is missing.");
            }

            ValidateRuntimeSupport(task, source, destination);

            var stagingRoot = Path.Combine(ResolveBasePath(_options.DataPath), "staging", request.RunId.ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            var completedSuccessfully = false;
            try
            {
                var sourcePath = await ResolveWorkingSourcePathAsync(source, stagingRoot, cancellationToken);
                var destinationPath = ResolveWorkingDestinationPathAsync(destination, stagingRoot);
                var sourceDisplayPath = GetDisplayPath(source, sourcePath);

                run.Status = "Running";
                run.StartedAt = DateTimeOffset.UtcNow;
                run.Message = $"Preparing {task.TaskType} task from {source.Name} to {destination.Name}.";
                await _repository.SaveRunAsync(run, cancellationToken);

                var transferResult = await TransferAsync(task, source, destination, sourcePath, destinationPath, cancellationToken);
                var destinationDisplayPath = transferResult.TargetPath;

                if (_smbTransferService.IsSmb(destination))
                {
                    destinationDisplayPath = transferResult.TargetIsDirectory
                        ? await _smbTransferService.UploadDirectoryAsync(destination, transferResult.TargetPath, task.VerifyDestination, cancellationToken)
                        : await _smbTransferService.UploadFileAsync(destination, transferResult.TargetPath, task.VerifyDestination, cancellationToken);
                }

                run.Status = "Succeeded";
                run.FinishedAt = DateTimeOffset.UtcNow;
                run.ProcessedItems = transferResult.ProcessedItems;
                run.ArtifactConnectionId = destination.Id;
                run.ArtifactKind = transferResult.TargetIsDirectory ? "Files" : "Archive";
                run.ArtifactName = transferResult.TargetIsDirectory ? string.Empty : Path.GetFileName(transferResult.TargetPath);
                run.ArtifactPath = destinationDisplayPath;
                run.Message = $"{GetTaskLabel(task.TaskType)} abgeschlossen: {sourceDisplayPath} -> {destinationDisplayPath} ({transferResult.Mode}).";
                await _repository.SaveRunAsync(run, cancellationToken);

                var deletedArtifacts = await ApplyRetentionAsync(task, destination, run, cancellationToken);
                if (deletedArtifacts > 0)
                {
                    run.Message = $"{run.Message} Retention: {deletedArtifacts} alte Archiv(e) geloescht.";
                    await _repository.SaveRunAsync(run, cancellationToken);
                }

                task.LastRunAt = run.FinishedAt;
                task.LastStatus = run.Status;
                task.LastMessage = run.Message;
                await _repository.SaveTaskAsync(task, cancellationToken);

                await _notificationService.TrySendAsync(task, run, source, destination, cancellationToken);
                _logger.LogInformation("Completed run {RunId} for task {TaskId}", run.Id, task.Id);
                completedSuccessfully = true;
            }
            finally
            {
                if (completedSuccessfully || !task.KeepLocalStagingOnFailure)
                {
                    TryDeleteDirectory(stagingRoot);
                }
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _logger.LogError(ex, "Task run failed for request {RunId}", request.RunId);

            var run = await _repository.GetRunByIdAsync(request.RunId, cancellationToken);
            if (run is null)
            {
                return;
            }

            run.Status = "Failed";
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Message = ex.Message;
            await _repository.SaveRunAsync(run, cancellationToken);

            var task = await _repository.GetTaskByIdAsync(request.TaskId, cancellationToken);
            if (task is not null)
            {
                task.LastRunAt = run.FinishedAt;
                task.LastStatus = run.Status;
                task.LastMessage = run.Message;
                await _repository.SaveTaskAsync(task, cancellationToken);

                var source = await _repository.GetConnectionByIdAsync(task.SourceConnectionId, cancellationToken);
                var destination = await _repository.GetConnectionByIdAsync(task.DestinationConnectionId, cancellationToken);
                await _notificationService.TrySendAsync(task, run, source, destination, cancellationToken);
            }
        }
    }

    private string ResolveConnectionPath(ConnectionProfile connection)
    {
        if (_smbTransferService.IsSmb(connection))
        {
            throw new InvalidOperationException("SMB-Verbindungen werden direkt ueber den SMB-Transfer verarbeitet.");
        }

        var normalizedPath = NormalizeFilesystemPath(ConnectionTypeCatalog.NormalizeRemotePath(connection.Type, connection.RemotePath));

        if (IsWindowsStylePath(normalizedPath))
        {
            throw new InvalidOperationException(
                $"Der Pfad '{connection.RemotePath}' ist ein Windows-/UNC-Pfad. Im Linux-Container muss das Ziel als gemountetes Verzeichnis erreichbar sein.");
        }

        if (Path.IsPathRooted(normalizedPath))
        {
            return Path.GetFullPath(normalizedPath);
        }

        var basePath = ResolveBasePath(_options.DataPath);
        var connectionRoot = Path.Combine(basePath, "filesystem", connection.Id.ToString());
        var fullPath = Path.GetFullPath(Path.Combine(connectionRoot, normalizedPath));

        if (!IsWithinRoot(connectionRoot, fullPath))
        {
            throw new InvalidOperationException($"Ungültiger Pfad für Verbindung {connection.Name}.");
        }

        return fullPath;
    }

    private async Task<string> ResolveWorkingSourcePathAsync(ConnectionProfile source, string stagingRoot, CancellationToken cancellationToken)
    {
        if (_smbTransferService.IsSmb(source))
        {
            var sourceRoot = Path.Combine(stagingRoot, "source");
            Directory.CreateDirectory(sourceRoot);
            await _smbTransferService.DownloadDirectoryAsync(source, sourceRoot, cancellationToken);
            return sourceRoot;
        }

        return ResolveConnectionPath(source);
    }

    private string ResolveWorkingDestinationPathAsync(ConnectionProfile destination, string stagingRoot)
    {
        if (_smbTransferService.IsSmb(destination))
        {
            var destinationRoot = Path.Combine(stagingRoot, "destination");
            Directory.CreateDirectory(destinationRoot);
            return destinationRoot;
        }

        return ResolveConnectionPath(destination);
    }

    private string GetDisplayPath(ConnectionProfile connection, string localPath)
    {
        return _smbTransferService.IsSmb(connection)
            ? _smbTransferService.GetRemoteDirectoryDisplayPath(connection)
            : localPath;
    }

    private async Task<TransferResult> TransferAsync(
        TaskDefinition task,
        ConnectionProfile source,
        ConnectionProfile destination,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(sourcePath))
        {
            return await TransferSingleFileAsync(task, source, destination, sourcePath, destinationPath, cancellationToken);
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Die Quelle wurde nicht gefunden: {sourcePath}");
        }

        var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"Die Quelle enthält keine Dateien: {sourcePath}");
        }

        Directory.CreateDirectory(destinationPath);

        if (ShouldCreateArchive(task))
        {
            var zipFileName = ArchiveFileNamePatternFormatter.BuildArchiveFileName(
                task.ArchiveFileNamePattern,
                task,
                source,
                destination,
                DateTimeOffset.Now);
            var zipPath = Path.Combine(destinationPath, zipFileName);
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(sourcePath, file).Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(file, relativePath, ResolveCompressionLevel(task.CompressionLevel));
            }

            return new TransferResult(files.Count, sourcePath, zipPath, $"ZIP/{TaskOptionCatalog.NormalizeCompressionLevel(task.CompressionLevel)}", false);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }

        return new TransferResult(files.Count, sourcePath, destinationPath, "direkt", true);
    }

    private Task<TransferResult> TransferSingleFileAsync(
        TaskDefinition task,
        ConnectionProfile source,
        ConnectionProfile destination,
        string sourceFile,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);

        if (ShouldCreateArchive(task))
        {
            var zipFileName = ArchiveFileNamePatternFormatter.BuildArchiveFileName(
                task.ArchiveFileNamePattern,
                task,
                source,
                destination,
                DateTimeOffset.Now);
            var zipPath = Path.Combine(destinationPath, zipFileName);
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(sourceFile, Path.GetFileName(sourceFile), ResolveCompressionLevel(task.CompressionLevel));
            return Task.FromResult(new TransferResult(1, sourceFile, zipPath, $"ZIP/{TaskOptionCatalog.NormalizeCompressionLevel(task.CompressionLevel)}", false));
        }

        var targetFile = Path.Combine(destinationPath, Path.GetFileName(sourceFile));
        File.Copy(sourceFile, targetFile, overwrite: true);
        return Task.FromResult(new TransferResult(1, sourceFile, targetFile, "direkt", false));
    }

    private string ResolveBasePath(string dataPath)
    {
        return Path.IsPathRooted(dataPath)
            ? dataPath
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, dataPath));
    }

    private static string NormalizeFilesystemPath(string path)
    {
        return path.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsWindowsStylePath(string path)
    {
        return path.StartsWith(@"\\", StringComparison.Ordinal) ||
               path.StartsWith("//", StringComparison.Ordinal) ||
               (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');
    }

    private static bool IsWithinRoot(string root, string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTaskLabel(string taskType)
    {
        return taskType.Equals("Sync", StringComparison.OrdinalIgnoreCase) ? "Synchronisation" : "Archivierung";
    }

    private static bool ShouldCreateArchive(TaskDefinition task)
    {
        return string.Equals(
            TaskOptionCatalog.NormalizeArchiveFormat(task.ArchiveFormat, task.CompressToZip),
            "Zip",
            StringComparison.OrdinalIgnoreCase);
    }

    private static CompressionLevel ResolveCompressionLevel(string? compressionLevel)
    {
        return TaskOptionCatalog.NormalizeCompressionLevel(compressionLevel) switch
        {
            "Fastest" => CompressionLevel.Fastest,
            "SmallestSize" => CompressionLevel.SmallestSize,
            "NoCompression" => CompressionLevel.NoCompression,
            _ => CompressionLevel.Optimal
        };
    }

    private static void ValidateRuntimeSupport(TaskDefinition task, ConnectionProfile source, ConnectionProfile destination)
    {
        if (!ConnectionTypeCatalog.CanRead(source))
        {
            throw new InvalidOperationException($"Die Source-Verbindung '{source.Name}' unterstuetzt kein Lesen.");
        }

        if (!ConnectionTypeCatalog.CanWrite(destination))
        {
            throw new InvalidOperationException($"Die Destination-Verbindung '{destination.Name}' unterstuetzt kein Schreiben.");
        }

        if (ConnectionTypeCatalog.IsEmail(source) || ConnectionTypeCatalog.IsEmail(destination))
        {
            throw new InvalidOperationException(
                "E-Mail-Transfers sind strukturell vorbereitet (IMAP/POP3 lesen, SMTP schreiben), aber der echte Mail-Connector ist noch nicht implementiert. Der Task wird deshalb nicht als erfolgreich markiert.");
        }

        if (string.Equals(TaskOptionCatalog.NormalizeTransferMode(task.TransferMode), "DirectStream", StringComparison.OrdinalIgnoreCase) &&
            ShouldCreateArchive(task) &&
            (ConnectionTypeCatalog.IsSmb(source) || ConnectionTypeCatalog.IsSmb(destination)))
        {
            throw new InvalidOperationException("DirectStream fuer SMB-ZIP-Archive ist noch nicht verfuegbar. Bitte den Transfermodus 'StagedLocal' verwenden.");
        }
    }

    private async Task<int> ApplyRetentionAsync(
        TaskDefinition task,
        ConnectionProfile destination,
        TaskRun currentRun,
        CancellationToken cancellationToken)
    {
        var mode = TaskRetentionPolicy.NormalizeRetentionMode(task.RetentionMode);
        if (mode == "None" || currentRun.ArtifactKind != "Archive")
        {
            return 0;
        }

        var runs = await _repository.GetRunsForTaskAsync(task.Id, cancellationToken);
        var artifacts = runs
            .Where(run =>
                run.Status == "Succeeded" &&
                run.ArtifactConnectionId == destination.Id &&
                run.ArtifactKind == "Archive" &&
                !string.IsNullOrWhiteSpace(run.ArtifactName) &&
                run.FinishedAt.HasValue)
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.QueuedAt)
            .ToList();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-(task.RetentionKeepDays ?? 30));
        var keepLast = task.RetentionKeepLast ?? 10;
        var candidates = artifacts
            .Select((run, index) => new { Run = run, Index = index })
            .Where(item =>
                item.Run.Id != currentRun.Id &&
                ShouldDeleteByRetention(mode, item.Index, item.Run.FinishedAt!.Value, keepLast, cutoff))
            .Select(item => item.Run)
            .ToList();

        var deleted = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryDeleteArtifactAsync(destination, candidate, cancellationToken))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private static bool ShouldDeleteByRetention(
        string mode,
        int index,
        DateTimeOffset finishedAt,
        int keepLast,
        DateTimeOffset cutoff)
    {
        return mode switch
        {
            "KeepLast" => index >= keepLast,
            "KeepDays" => finishedAt < cutoff,
            "KeepLastAndDays" => index >= keepLast && finishedAt < cutoff,
            _ => false
        };
    }

    private async Task<bool> TryDeleteArtifactAsync(ConnectionProfile destination, TaskRun run, CancellationToken cancellationToken)
    {
        try
        {
            if (_smbTransferService.IsSmb(destination))
            {
                await _smbTransferService.DeleteFileAsync(destination, run.ArtifactName, cancellationToken);
                return true;
            }

            var destinationRoot = ResolveConnectionPath(destination);
            var artifactPath = Path.GetFullPath(run.ArtifactPath);
            if (!IsWithinRoot(destinationRoot, artifactPath) || !File.Exists(artifactPath))
            {
                return false;
            }

            File.Delete(artifactPath);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Retention could not delete artifact {ArtifactPath}", run.ArtifactPath);
            return false;
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record TransferResult(long ProcessedItems, string SourcePath, string TargetPath, string Mode, bool TargetIsDirectory);
}
