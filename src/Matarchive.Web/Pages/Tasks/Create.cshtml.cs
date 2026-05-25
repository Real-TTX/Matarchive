using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Pages.Tasks;

public sealed class CreateModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public CreateModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    [BindProperty]
    [Required]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [BindProperty]
    [Required]
    [Display(Name = "Task-Typ")]
    public string TaskType { get; set; } = MatarchiveConstants.TaskTypes[0];

    [BindProperty]
    [Required]
    [Display(Name = "Source")]
    public Guid? SourceConnectionId { get; set; }

    [BindProperty]
    [Required]
    [Display(Name = "Destination")]
    public Guid? DestinationConnectionId { get; set; }

    [BindProperty]
    [Display(Name = "ZIP erzeugen")]
    public bool CompressToZip { get; set; } = true;

    [BindProperty]
    [Display(Name = "Archivformat")]
    public string ArchiveFormat { get; set; } = "Zip";

    [BindProperty]
    [Display(Name = "Kompression")]
    public string CompressionLevel { get; set; } = "Optimal";

    [BindProperty]
    [Display(Name = "Transfermodus")]
    public string TransferMode { get; set; } = "StagedLocal";

    [BindProperty]
    [Display(Name = "Ziel nach Upload verifizieren")]
    public bool VerifyDestination { get; set; } = true;

    [BindProperty]
    [Display(Name = "Staging bei Fehler behalten")]
    public bool KeepLocalStagingOnFailure { get; set; }

    [BindProperty]
    [Display(Name = "Archiv-Dateinamensmuster")]
    public string ArchiveFileNamePattern { get; set; } = MatarchiveConstants.DefaultArchiveFileNamePattern;

    [BindProperty]
    [Display(Name = "Aktiv")]
    public bool Enabled { get; set; } = true;

    [BindProperty]
    [Display(Name = "Intervall in Minuten")]
    public int? RunEveryMinutes { get; set; }

    [BindProperty]
    [Display(Name = "Planung")]
    public string ScheduleMode { get; set; } = "Manual";

    [BindProperty]
    [Display(Name = "Intervall in Minuten")]
    public int? ScheduleIntervalMinutes { get; set; }

    [BindProperty]
    [Display(Name = "Uhrzeit")]
    public string ScheduleTime { get; set; } = "02:00";

    [BindProperty]
    [Display(Name = "Wochentage")]
    public string[] SelectedScheduleDays { get; set; } = [];

    [BindProperty]
    [Display(Name = "Zeitzone")]
    public string ScheduleTimeZoneId { get; set; } = MatarchiveConstants.DefaultScheduleTimeZoneId;

    [BindProperty]
    [Display(Name = "Retention")]
    public string RetentionMode { get; set; } = "None";

    [BindProperty]
    [Display(Name = "Archive behalten")]
    public int? RetentionKeepLast { get; set; } = 10;

    [BindProperty]
    [Display(Name = "Tage behalten")]
    public int? RetentionKeepDays { get; set; } = 30;

    [BindProperty]
    [Display(Name = "Notizen")]
    public string Notes { get; set; } = "";

    public IReadOnlyList<SelectListItem> SourceConnectionOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> DestinationConnectionOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> TaskTypeOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> ArchiveFormatOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> CompressionLevelOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> TransferModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> ScheduleModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> WeekdayOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> RetentionModeOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
        var settings = await _repository.GetNotificationSettingsAsync();
        ArchiveFileNamePattern = string.IsNullOrWhiteSpace(settings.DefaultArchiveFileNamePattern)
            ? MatarchiveConstants.DefaultArchiveFileNamePattern
            : settings.DefaultArchiveFileNamePattern;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var connections = await LoadOptionsAsync();
        NormalizeOptions();
        ValidateTaskInput(connections);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var task = new TaskDefinition
        {
            Name = Name.Trim(),
            TaskType = TaskType,
            SourceConnectionId = SourceConnectionId!.Value,
            DestinationConnectionId = DestinationConnectionId!.Value,
            CompressToZip = CompressToZip,
            ArchiveFormat = ArchiveFormat,
            CompressionLevel = CompressionLevel,
            TransferMode = TransferMode,
            VerifyDestination = VerifyDestination,
            KeepLocalStagingOnFailure = KeepLocalStagingOnFailure,
            ArchiveFileNamePattern = ArchiveFileNamePattern.Trim(),
            Enabled = Enabled,
            RunEveryMinutes = ScheduleMode == "Interval" ? NormalizeInterval(ScheduleIntervalMinutes) : null,
            ScheduleMode = ScheduleMode,
            ScheduleIntervalMinutes = NormalizeInterval(ScheduleIntervalMinutes),
            ScheduleTime = TaskSchedulePolicy.NormalizeScheduleTime(ScheduleTime),
            ScheduleDays = TaskSchedulePolicy.NormalizeScheduleDays(SelectedScheduleDays),
            ScheduleTimeZoneId = string.IsNullOrWhiteSpace(ScheduleTimeZoneId) ? MatarchiveConstants.DefaultScheduleTimeZoneId : ScheduleTimeZoneId.Trim(),
            RetentionMode = RetentionMode,
            RetentionKeepLast = TaskRetentionPolicy.NormalizePositive(RetentionKeepLast) ?? 10,
            RetentionKeepDays = TaskRetentionPolicy.NormalizePositive(RetentionKeepDays) ?? 30,
            Notes = Notes.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastStatus = "Never run"
        };

        await _repository.SaveTaskAsync(task);
        FlashSuccess = $"Task {task.Name} wurde angelegt.";
        return RedirectToPage("/Tasks/Index");
    }

    private async Task<List<ConnectionProfile>> LoadOptionsAsync()
    {
        var connections = await _repository.GetConnectionsAsync();
        SourceConnectionOptions = connections
            .Where(ConnectionTypeCatalog.CanRead)
            .OrderBy(connection => connection.Name)
            .Select(connection => new SelectListItem(BuildConnectionOptionLabel(connection), connection.Id.ToString()))
            .ToList();

        DestinationConnectionOptions = connections
            .Where(ConnectionTypeCatalog.CanWrite)
            .OrderBy(connection => connection.Name)
            .Select(connection => new SelectListItem(BuildConnectionOptionLabel(connection), connection.Id.ToString()))
            .ToList();

        TaskTypeOptions = MatarchiveConstants.TaskTypes
            .Select(taskType => new SelectListItem(taskType, taskType))
            .ToList();

        ArchiveFormatOptions = TaskOptionCatalog.ArchiveFormatOptions();
        CompressionLevelOptions = TaskOptionCatalog.CompressionLevelOptions();
        TransferModeOptions = TaskOptionCatalog.TransferModeOptions();
        ScheduleModeOptions = TaskSchedulePolicy.ScheduleModeOptions();
        WeekdayOptions = TaskSchedulePolicy.WeekdayOptions();
        RetentionModeOptions = TaskRetentionPolicy.RetentionModeOptions();

        return connections;
    }

    private void NormalizeOptions()
    {
        ArchiveFormat = TaskOptionCatalog.NormalizeArchiveFormat(ArchiveFormat, CompressToZip);
        CompressionLevel = TaskOptionCatalog.NormalizeCompressionLevel(CompressionLevel);
        TransferMode = TaskOptionCatalog.NormalizeTransferMode(TransferMode);
        CompressToZip = string.Equals(ArchiveFormat, "Zip", StringComparison.OrdinalIgnoreCase);
        ScheduleMode = TaskSchedulePolicy.NormalizeScheduleMode(ScheduleMode, ScheduleIntervalMinutes);
        SelectedScheduleDays = TaskSchedulePolicy.NormalizeScheduleDays(SelectedScheduleDays)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        RetentionMode = TaskRetentionPolicy.NormalizeRetentionMode(RetentionMode);
    }

    private void ValidateTaskInput(IReadOnlyList<ConnectionProfile> connections)
    {
        if (!MatarchiveConstants.TaskTypes.Contains(TaskType, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(TaskType), "Bitte einen gueltigen Task-Typ waehlen.");
        }

        if (SourceConnectionId == DestinationConnectionId)
        {
            ModelState.AddModelError(string.Empty, "Source und Destination muessen unterschiedlich sein.");
        }

        var source = connections.FirstOrDefault(connection => connection.Id == SourceConnectionId);
        var destination = connections.FirstOrDefault(connection => connection.Id == DestinationConnectionId);
        if (SourceConnectionId.HasValue && source is null)
        {
            ModelState.AddModelError(nameof(SourceConnectionId), "Source wurde nicht gefunden.");
        }

        if (DestinationConnectionId.HasValue && destination is null)
        {
            ModelState.AddModelError(nameof(DestinationConnectionId), "Destination wurde nicht gefunden.");
        }

        if (source is not null && !ConnectionTypeCatalog.CanRead(source))
        {
            ModelState.AddModelError(nameof(SourceConnectionId), "Die Source-Verbindung muss Lesen unterstuetzen.");
        }

        if (destination is not null && !ConnectionTypeCatalog.CanWrite(destination))
        {
            ModelState.AddModelError(nameof(DestinationConnectionId), "Die Destination-Verbindung muss Schreiben unterstuetzen.");
        }

        if (!TaskOptionCatalog.IsArchiveFormat(ArchiveFormat))
        {
            ModelState.AddModelError(nameof(ArchiveFormat), "Bitte ein gueltiges Archivformat waehlen.");
        }

        if (!TaskOptionCatalog.IsCompressionLevel(CompressionLevel))
        {
            ModelState.AddModelError(nameof(CompressionLevel), "Bitte eine gueltige Kompression waehlen.");
        }

        if (!TaskOptionCatalog.IsTransferMode(TransferMode))
        {
            ModelState.AddModelError(nameof(TransferMode), "Bitte einen gueltigen Transfermodus waehlen.");
        }

        if (string.Equals(ArchiveFormat, "Zip", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(ArchiveFileNamePattern))
        {
            ModelState.AddModelError(nameof(ArchiveFileNamePattern), "Das Dateinamensmuster ist fuer ZIP-Archive erforderlich.");
        }

        if (string.Equals(TransferMode, "DirectStream", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ArchiveFormat, "Zip", StringComparison.OrdinalIgnoreCase) &&
            (ConnectionTypeCatalog.IsSmb(source) || ConnectionTypeCatalog.IsSmb(destination)))
        {
            ModelState.AddModelError(nameof(TransferMode), "Direktes Streaming fuer SMB-ZIP-Archive ist noch nicht verfuegbar. Bitte Staging verwenden.");
        }

        ValidateScheduleInput();
        ValidateRetentionInput();
    }

    private void ValidateScheduleInput()
    {
        if (!MatarchiveConstants.ScheduleModes.Contains(ScheduleMode, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ScheduleMode), "Bitte eine gueltige Planung waehlen.");
        }

        if (ScheduleMode == "Interval" && ScheduleIntervalMinutes.GetValueOrDefault() <= 0)
        {
            ModelState.AddModelError(nameof(ScheduleIntervalMinutes), "Das Intervall muss mindestens 1 Minute betragen.");
        }

        if ((ScheduleMode == "Daily" || ScheduleMode == "Weekly") &&
            !TimeOnly.TryParseExact(ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            ModelState.AddModelError(nameof(ScheduleTime), "Bitte eine Uhrzeit im Format HH:mm eintragen.");
        }

        if (ScheduleMode == "Weekly" && SelectedScheduleDays.Length == 0)
        {
            ModelState.AddModelError(nameof(SelectedScheduleDays), "Bitte mindestens einen Wochentag auswaehlen.");
        }
    }

    private void ValidateRetentionInput()
    {
        if (!MatarchiveConstants.RetentionModes.Contains(RetentionMode, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(RetentionMode), "Bitte eine gueltige Retention waehlen.");
        }

        if ((RetentionMode == "KeepLast" || RetentionMode == "KeepLastAndDays") &&
            RetentionKeepLast.GetValueOrDefault() <= 0)
        {
            ModelState.AddModelError(nameof(RetentionKeepLast), "Die Anzahl muss mindestens 1 sein.");
        }

        if ((RetentionMode == "KeepDays" || RetentionMode == "KeepLastAndDays") &&
            RetentionKeepDays.GetValueOrDefault() <= 0)
        {
            ModelState.AddModelError(nameof(RetentionKeepDays), "Die Tage muessen mindestens 1 sein.");
        }
    }

    private static int? NormalizeInterval(int? minutes)
    {
        return minutes.HasValue && minutes.Value > 0 ? minutes : null;
    }

    private static string BuildConnectionOptionLabel(ConnectionProfile connection)
    {
        var descriptor = ConnectionTypeCatalog.GetDescriptor(connection.Type);
        return $"{connection.Name} ({descriptor.DisplayName}, {ConnectionTypeCatalog.GetCapabilitySummary(connection)})";
    }
}
