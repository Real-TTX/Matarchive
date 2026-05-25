using System.Text.Json;
using Matarchive.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Matarchive.Web.Infrastructure;

public sealed class MatarchiveRepository
{
    private readonly JsonFileStore<List<AppUser>> _users;
    private readonly JsonFileStore<List<ConnectionProfile>> _connections;
    private readonly JsonFileStore<List<TaskDefinition>> _tasks;
    private readonly JsonFileStore<List<TaskRun>> _runs;
    private readonly JsonFileStore<List<ApiKeyRecord>> _apiKeys;
    private readonly JsonFileStore<NotificationSettings> _settings;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly MatarchiveOptions _options;
    private readonly ILogger<MatarchiveRepository> _logger;
    private readonly string _dataDirectory;

    public MatarchiveRepository(
        IHostEnvironment environment,
        IOptions<MatarchiveOptions> options,
        IPasswordHasher<AppUser> passwordHasher,
        ILogger<MatarchiveRepository> logger)
    {
        _options = options.Value;
        _passwordHasher = passwordHasher;
        _logger = logger;

        _dataDirectory = Path.IsPathRooted(_options.DataPath)
            ? _options.DataPath
            : Path.Combine(environment.ContentRootPath, _options.DataPath);

        _users = new JsonFileStore<List<AppUser>>(Path.Combine(_dataDirectory, "users.json"), () => []);
        _connections = new JsonFileStore<List<ConnectionProfile>>(Path.Combine(_dataDirectory, "connections.json"), () => []);
        _tasks = new JsonFileStore<List<TaskDefinition>>(Path.Combine(_dataDirectory, "tasks.json"), () => []);
        _runs = new JsonFileStore<List<TaskRun>>(Path.Combine(_dataDirectory, "runs.json"), () => []);
        _apiKeys = new JsonFileStore<List<ApiKeyRecord>>(Path.Combine(_dataDirectory, "api-keys.json"), () => []);
        _settings = new JsonFileStore<NotificationSettings>(Path.Combine(_dataDirectory, "notification-settings.json"), () => new NotificationSettings());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dataDirectory);

        var users = await _users.ReadAsync(cancellationToken);
        if (users.Count == 0)
        {
            var admin = new AppUser
            {
                Username = _options.InitialAdminUsername.Trim(),
                DisplayName = "Primary Administrator",
                IsAdmin = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            admin.PasswordHash = _passwordHasher.HashPassword(admin, _options.InitialAdminPassword);
            users.Add(admin);
            await _users.WriteAsync(users, cancellationToken);
            _logger.LogInformation("Seeded default admin user {UserName}", admin.Username);
        }

        var settings = await _settings.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.FromName))
        {
            settings.FromName = "Matarchive";
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultArchiveFileNamePattern))
        {
            settings.DefaultArchiveFileNamePattern = MatarchiveConstants.DefaultArchiveFileNamePattern;
        }

        await _settings.WriteAsync(settings, cancellationToken);

        var connections = await _connections.ReadAsync(cancellationToken);
        var connectionsChanged = false;
        foreach (var connection in connections)
        {
            var originalType = connection.Type;
            var normalizedType = ConnectionTypeCatalog.Normalize(connection.Type);
            if (!string.Equals(connection.Type, normalizedType, StringComparison.Ordinal))
            {
                connection.Type = normalizedType;
                connectionsChanged = true;
            }

            if (ConnectionTypeCatalog.ApplyDefaults(connection, originalType, forceTypeDefaults: !connection.CapabilitiesConfigured))
            {
                connectionsChanged = true;
            }

            if (!connection.CapabilitiesConfigured)
            {
                connection.CapabilitiesConfigured = true;
                connectionsChanged = true;
            }
        }

        if (connectionsChanged)
        {
            await _connections.WriteAsync(connections, cancellationToken);
        }

        var tasks = await _tasks.ReadAsync(cancellationToken);
        var tasksChanged = false;
        foreach (var task in tasks)
        {
            var normalizedArchiveFormat = TaskOptionCatalog.NormalizeArchiveFormat(task.ArchiveFormat, task.CompressToZip);
            if (!string.Equals(task.ArchiveFormat, normalizedArchiveFormat, StringComparison.Ordinal))
            {
                task.ArchiveFormat = normalizedArchiveFormat;
                task.CompressToZip = string.Equals(normalizedArchiveFormat, "Zip", StringComparison.OrdinalIgnoreCase);
                tasksChanged = true;
            }

            var normalizedCompressionLevel = TaskOptionCatalog.NormalizeCompressionLevel(task.CompressionLevel);
            if (!string.Equals(task.CompressionLevel, normalizedCompressionLevel, StringComparison.Ordinal))
            {
                task.CompressionLevel = normalizedCompressionLevel;
                tasksChanged = true;
            }

            var normalizedTransferMode = TaskOptionCatalog.NormalizeTransferMode(task.TransferMode);
            if (!string.Equals(task.TransferMode, normalizedTransferMode, StringComparison.Ordinal))
            {
                task.TransferMode = normalizedTransferMode;
                tasksChanged = true;
            }

            var normalizedScheduleMode = TaskSchedulePolicy.NormalizeScheduleMode(task.ScheduleMode, task.RunEveryMinutes);
            if (!string.Equals(task.ScheduleMode, normalizedScheduleMode, StringComparison.Ordinal))
            {
                task.ScheduleMode = normalizedScheduleMode;
                tasksChanged = true;
            }

            if (task.ScheduleIntervalMinutes is null && task.RunEveryMinutes.GetValueOrDefault() > 0)
            {
                task.ScheduleIntervalMinutes = task.RunEveryMinutes;
                tasksChanged = true;
            }

            var normalizedScheduleTime = TaskSchedulePolicy.NormalizeScheduleTime(task.ScheduleTime);
            if (!string.Equals(task.ScheduleTime, normalizedScheduleTime, StringComparison.Ordinal))
            {
                task.ScheduleTime = normalizedScheduleTime;
                tasksChanged = true;
            }

            if (string.IsNullOrWhiteSpace(task.ScheduleTimeZoneId))
            {
                task.ScheduleTimeZoneId = MatarchiveConstants.DefaultScheduleTimeZoneId;
                tasksChanged = true;
            }

            var normalizedRetentionMode = TaskRetentionPolicy.NormalizeRetentionMode(task.RetentionMode);
            if (!string.Equals(task.RetentionMode, normalizedRetentionMode, StringComparison.Ordinal))
            {
                task.RetentionMode = normalizedRetentionMode;
                tasksChanged = true;
            }

            var normalizedKeepLast = TaskRetentionPolicy.NormalizePositive(task.RetentionKeepLast) ?? 10;
            if (task.RetentionKeepLast != normalizedKeepLast)
            {
                task.RetentionKeepLast = normalizedKeepLast;
                tasksChanged = true;
            }

            var normalizedKeepDays = TaskRetentionPolicy.NormalizePositive(task.RetentionKeepDays) ?? 30;
            if (task.RetentionKeepDays != normalizedKeepDays)
            {
                task.RetentionKeepDays = normalizedKeepDays;
                tasksChanged = true;
            }

            if (string.IsNullOrWhiteSpace(task.ArchiveFileNamePattern))
            {
                task.ArchiveFileNamePattern = settings.DefaultArchiveFileNamePattern;
                tasksChanged = true;
            }
        }

        if (tasksChanged)
        {
            await _tasks.WriteAsync(tasks, cancellationToken);
        }
    }

    public Task<List<AppUser>> GetUsersAsync(CancellationToken cancellationToken = default) => _users.ReadAsync(cancellationToken);

    public async Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(cancellationToken);
        return users.FirstOrDefault(user => user.Id == id);
    }

    public async Task<AppUser?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim();
        var users = await GetUsersAsync(cancellationToken);
        return users.FirstOrDefault(user => string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveUserAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(cancellationToken);
        Upsert(users, candidate => candidate.Id == user.Id, user);
        await _users.WriteAsync(users, cancellationToken);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(cancellationToken);
        users.RemoveAll(user => user.Id == id);
        await _users.WriteAsync(users, cancellationToken);
    }

    public Task<List<ConnectionProfile>> GetConnectionsAsync(CancellationToken cancellationToken = default) => _connections.ReadAsync(cancellationToken);

    public async Task<ConnectionProfile?> GetConnectionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connections = await GetConnectionsAsync(cancellationToken);
        return connections.FirstOrDefault(connection => connection.Id == id);
    }

    public async Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default)
    {
        var connections = await GetConnectionsAsync(cancellationToken);
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        if (connection.CreatedAt == default)
        {
            connection.CreatedAt = DateTimeOffset.UtcNow;
        }
        Upsert(connections, candidate => candidate.Id == connection.Id, connection);
        await _connections.WriteAsync(connections, cancellationToken);
    }

    public async Task DeleteConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connections = await GetConnectionsAsync(cancellationToken);
        connections.RemoveAll(connection => connection.Id == id);
        await _connections.WriteAsync(connections, cancellationToken);
    }

    public Task<List<TaskDefinition>> GetTasksAsync(CancellationToken cancellationToken = default) => _tasks.ReadAsync(cancellationToken);

    public async Task<TaskDefinition?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tasks = await GetTasksAsync(cancellationToken);
        return tasks.FirstOrDefault(task => task.Id == id);
    }

    public async Task SaveTaskAsync(TaskDefinition task, CancellationToken cancellationToken = default)
    {
        var tasks = await GetTasksAsync(cancellationToken);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        if (task.CreatedAt == default)
        {
            task.CreatedAt = DateTimeOffset.UtcNow;
        }
        Upsert(tasks, candidate => candidate.Id == task.Id, task);
        await _tasks.WriteAsync(tasks, cancellationToken);
    }

    public async Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tasks = await GetTasksAsync(cancellationToken);
        tasks.RemoveAll(task => task.Id == id);
        await _tasks.WriteAsync(tasks, cancellationToken);
    }

    public async Task DeleteRunsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var runs = await GetRunsAsync(cancellationToken);
        runs.RemoveAll(run => run.TaskId == taskId);
        await _runs.WriteAsync(runs, cancellationToken);
    }

    public Task<List<TaskRun>> GetRunsAsync(CancellationToken cancellationToken = default) => _runs.ReadAsync(cancellationToken);

    public async Task<TaskRun?> GetRunByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var runs = await GetRunsAsync(cancellationToken);
        return runs.FirstOrDefault(run => run.Id == id);
    }

    public async Task<List<TaskRun>> GetRunsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var runs = await GetRunsAsync(cancellationToken);
        return runs.Where(run => run.TaskId == taskId).OrderByDescending(run => run.QueuedAt).ToList();
    }

    public async Task SaveRunAsync(TaskRun run, CancellationToken cancellationToken = default)
    {
        var runs = await GetRunsAsync(cancellationToken);
        Upsert(runs, candidate => candidate.Id == run.Id, run);
        await _runs.WriteAsync(runs, cancellationToken);
    }

    public async Task DeleteRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var runs = await GetRunsAsync(cancellationToken);
        runs.RemoveAll(run => run.Id == id);
        await _runs.WriteAsync(runs, cancellationToken);
    }

    public Task<List<ApiKeyRecord>> GetApiKeysAsync(CancellationToken cancellationToken = default) => _apiKeys.ReadAsync(cancellationToken);

    public async Task<ApiKeyRecord?> GetApiKeyByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var keys = await GetApiKeysAsync(cancellationToken);
        return keys.FirstOrDefault(key => key.Id == id);
    }

    public async Task SaveApiKeyAsync(ApiKeyRecord key, CancellationToken cancellationToken = default)
    {
        var keys = await GetApiKeysAsync(cancellationToken);
        Upsert(keys, candidate => candidate.Id == key.Id, key);
        await _apiKeys.WriteAsync(keys, cancellationToken);
    }

    public async Task DeleteApiKeyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var keys = await GetApiKeysAsync(cancellationToken);
        keys.RemoveAll(key => key.Id == id);
        await _apiKeys.WriteAsync(keys, cancellationToken);
    }

    public Task<NotificationSettings> GetNotificationSettingsAsync(CancellationToken cancellationToken = default) => _settings.ReadAsync(cancellationToken);

    public Task SaveNotificationSettingsAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
        => _settings.WriteAsync(settings, cancellationToken);

    public static void Upsert<T>(List<T> items, Func<T, bool> match, T value)
    {
        var index = items.FindIndex(item => match(item));
        if (index >= 0)
        {
            items[index] = value;
            return;
        }

        items.Add(value);
    }
}
