using System.Text.Json.Serialization;

namespace Matarchive.Web.Domain;

public static class MatarchiveConstants
{
    public const string AdminRole = "Admin";

    public static readonly string[] ConnectionTypes = ["EMAIL", "SMB", "CUSTOM"];
    public static readonly string[] MailIncomingProtocols = ["IMAP", "POP3"];
    public static readonly string[] MailOutgoingProtocols = ["SMTP"];
    public static readonly string[] TaskTypes = ["Archive", "Sync"];
    public static readonly string[] TaskStatuses = ["Queued", "Running", "Succeeded", "Failed"];
    public static readonly string[] ArchiveFormats = ["Zip", "None"];
    public static readonly string[] CompressionLevels = ["Optimal", "Fastest", "SmallestSize", "NoCompression"];
    public static readonly string[] TransferModes = ["StagedLocal", "DirectStream"];
    public static readonly string[] ScheduleModes = ["Manual", "Interval", "Daily", "Weekly"];
    public static readonly string[] RetentionModes = ["None", "KeepLast", "KeepDays", "KeepLastAndDays"];
    public static readonly string[] ApiKeyHeaderNames = ["X-Matarchive-Api-Key", "X-Api-Key", "Authorization"];
    public const string DefaultArchiveFileNamePattern = "Matarchive_{TaskName}_yyyyMMdd_HHmmss";
    public const string DefaultScheduleTimeZoneId = "Europe/Berlin";
}

public sealed class MatarchiveOptions
{
    public string DataPath { get; set; } = "data";
    public string BaseUrl { get; set; } = "http://localhost:8077";
    public string InitialAdminUsername { get; set; } = "admin";
    public string InitialAdminPassword { get; set; } = "ChangeMe!123";
}

public sealed class NotificationSettings
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Matarchive";
    public string RecipientsCsv { get; set; } = "";
    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFailure { get; set; } = true;
    public string DefaultArchiveFileNamePattern { get; set; } = MatarchiveConstants.DefaultArchiveFileNamePattern;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "EMAIL";
    public bool CapabilitiesConfigured { get; set; }
    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; }
    public string IncomingProtocol { get; set; } = "IMAP";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 110;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "";
    public string Secret { get; set; } = "";
    public string OutgoingProtocol { get; set; } = "SMTP";
    public string OutgoingHost { get; set; } = "";
    public int? OutgoingPort { get; set; } = 587;
    public bool OutgoingUseSsl { get; set; } = true;
    public string OutgoingUsername { get; set; } = "";
    public string OutgoingSecret { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TaskType { get; set; } = "Archive";
    public Guid SourceConnectionId { get; set; }
    public Guid DestinationConnectionId { get; set; }
    public bool CompressToZip { get; set; } = true;
    public string ArchiveFormat { get; set; } = "Zip";
    public string CompressionLevel { get; set; } = "Optimal";
    public string TransferMode { get; set; } = "StagedLocal";
    public bool VerifyDestination { get; set; } = true;
    public bool KeepLocalStagingOnFailure { get; set; }
    public string ArchiveFileNamePattern { get; set; } = MatarchiveConstants.DefaultArchiveFileNamePattern;
    public bool Enabled { get; set; } = true;
    public int? RunEveryMinutes { get; set; }
    public string ScheduleMode { get; set; } = "Manual";
    public int? ScheduleIntervalMinutes { get; set; }
    public string ScheduleTime { get; set; } = "02:00";
    public string ScheduleDays { get; set; } = "";
    public string ScheduleTimeZoneId { get; set; } = MatarchiveConstants.DefaultScheduleTimeZoneId;
    public string RetentionMode { get; set; } = "None";
    public int? RetentionKeepLast { get; set; } = 10;
    public int? RetentionKeepDays { get; set; } = 30;
    public string Notes { get; set; } = "";
    public string LastStatus { get; set; } = "Never run";
    public DateTimeOffset? LastRunAt { get; set; }
    public string LastMessage { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public string Trigger { get; set; } = "Manual";
    public string Status { get; set; } = "Queued";
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Message { get; set; } = "";
    public long? ProcessedItems { get; set; }
    public Guid? ArtifactConnectionId { get; set; }
    public string ArtifactKind { get; set; } = "";
    public string ArtifactName { get; set; } = "";
    public string ArtifactPath { get; set; } = "";
}

public sealed class ApiKeyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Hash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class IssuedApiKey
{
    public required ApiKeyRecord Record { get; init; }
    public required string Secret { get; init; }
}

public sealed class ApiTaskStatusDto
{
    public Guid TaskId { get; set; }
    public string Name { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? LastRunAt { get; set; }
    public string LastMessage { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class ApiStatusResponse
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationName { get; set; } = "Matarchive";
    public string BaseUrl { get; set; } = "";
    public int TaskCount { get; set; }
    public int ConnectionCount { get; set; }
    public int ApiKeyCount { get; set; }
    public IReadOnlyList<ApiTaskStatusDto> Tasks { get; set; } = [];
}

public sealed class TaskRunRequest
{
    public TaskRunRequest(Guid taskId, Guid runId, string trigger)
    {
        TaskId = taskId;
        RunId = runId;
        Trigger = trigger;
    }

    public Guid TaskId { get; }
    public Guid RunId { get; }
    public string Trigger { get; }
}
