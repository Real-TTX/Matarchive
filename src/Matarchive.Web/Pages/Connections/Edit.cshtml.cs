using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Matarchive.Web.Pages.Connections;

public sealed class EditModel : ConnectionFormPageModel
{
    private readonly MatarchiveRepository _repository;

    public EditModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    [BindProperty]
    public Guid Id { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        PrepareTypeOptions();
        var connection = await _repository.GetConnectionByIdAsync(id);
        if (connection is null)
        {
            return NotFound();
        }

        Id = connection.Id;
        OriginalType = connection.Type;
        OriginalPort = connection.Port;
        Secret = string.Empty;
        OutgoingSecret = string.Empty;
        LoadFromProfile(connection, keepSecrets: false);

        ApplyTypeDefaults();
        Secret = string.Empty;
        OutgoingSecret = string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        PrepareTypeOptions();
        ApplyTypeDefaults();

        if (!ValidateConnectionInput(requireIncomingSecret: false, requireOutgoingSecret: false))
        {
            return Page();
        }

        var connection = await _repository.GetConnectionByIdAsync(Id);
        if (connection is null)
        {
            return NotFound();
        }

        var submitted = ToProfile();
        ConnectionTypeCatalog.ApplyDefaults(submitted, OriginalType);

        connection.Name = submitted.Name;
        connection.Type = submitted.Type;
        connection.CapabilitiesConfigured = true;
        connection.CanRead = submitted.CanRead;
        connection.CanWrite = submitted.CanWrite;
        connection.IncomingProtocol = submitted.IncomingProtocol;
        connection.Host = submitted.Host;
        connection.Port = submitted.Port;
        connection.UseSsl = submitted.UseSsl;
        connection.Username = submitted.Username;
        if (!string.IsNullOrWhiteSpace(Secret))
        {
            connection.Secret = Secret;
        }

        connection.OutgoingProtocol = submitted.OutgoingProtocol;
        connection.OutgoingHost = submitted.OutgoingHost;
        connection.OutgoingPort = submitted.OutgoingPort;
        connection.OutgoingUseSsl = submitted.OutgoingUseSsl;
        connection.OutgoingUsername = submitted.OutgoingUsername;
        if (!string.IsNullOrWhiteSpace(OutgoingSecret))
        {
            connection.OutgoingSecret = OutgoingSecret;
        }

        connection.RemotePath = submitted.RemotePath;
        connection.Notes = submitted.Notes;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveConnectionAsync(connection);

        FlashSuccess = $"Verbindung {connection.Name} wurde gespeichert.";
        return RedirectToPage("/Connections/Index");
    }
}
