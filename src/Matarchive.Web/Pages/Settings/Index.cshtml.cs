using System.ComponentModel.DataAnnotations;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Settings;

public sealed class IndexModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public IndexModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    [BindProperty]
    [Display(Name = "SMTP-Host")]
    public string SmtpHost { get; set; } = "";

    [BindProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "SMTP-Port")]
    public int SmtpPort { get; set; } = 587;

    [BindProperty]
    [Display(Name = "Benutzername")]
    public string Username { get; set; } = "";

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "Passwort")]
    public string Password { get; set; } = "";

    [BindProperty]
    [Display(Name = "Absenderadresse")]
    public string FromAddress { get; set; } = "";

    [BindProperty]
    [Display(Name = "Absendername")]
    public string FromName { get; set; } = "";

    [BindProperty]
    [Display(Name = "Empfänger")]
    public string RecipientsCsv { get; set; } = "";

    [BindProperty]
    [Display(Name = "SSL/TLS")]
    public bool UseSsl { get; set; } = true;

    [BindProperty]
    [Display(Name = "Erfolge melden")]
    public bool NotifyOnSuccess { get; set; }

    [BindProperty]
    [Display(Name = "Fehler melden")]
    public bool NotifyOnFailure { get; set; } = true;

    [BindProperty]
    [Required]
    [Display(Name = "Archiv-Dateinamensmuster")]
    public string DefaultArchiveFileNamePattern { get; set; } = MatarchiveConstants.DefaultArchiveFileNamePattern;

    public async Task OnGetAsync()
    {
        var settings = await _repository.GetNotificationSettingsAsync();
        SmtpHost = settings.SmtpHost;
        SmtpPort = settings.SmtpPort;
        Username = settings.Username;
        Password = settings.Password;
        FromAddress = settings.FromAddress;
        FromName = settings.FromName;
        RecipientsCsv = settings.RecipientsCsv;
        UseSsl = settings.UseSsl;
        NotifyOnSuccess = settings.NotifyOnSuccess;
        NotifyOnFailure = settings.NotifyOnFailure;
        DefaultArchiveFileNamePattern = string.IsNullOrWhiteSpace(settings.DefaultArchiveFileNamePattern)
            ? MatarchiveConstants.DefaultArchiveFileNamePattern
            : settings.DefaultArchiveFileNamePattern;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var settings = new NotificationSettings
        {
            SmtpHost = SmtpHost.Trim(),
            SmtpPort = SmtpPort,
            Username = Username.Trim(),
            Password = Password,
            FromAddress = FromAddress.Trim(),
            FromName = FromName.Trim(),
            RecipientsCsv = RecipientsCsv.Trim(),
            UseSsl = UseSsl,
            NotifyOnSuccess = NotifyOnSuccess,
            NotifyOnFailure = NotifyOnFailure,
            DefaultArchiveFileNamePattern = ArchiveFileNamePatternFormatter.Normalize(DefaultArchiveFileNamePattern)
        };

        await _repository.SaveNotificationSettingsAsync(settings);
        FlashSuccess = "Benachrichtigungseinstellungen wurden gespeichert.";
        return RedirectToPage();
    }
}
