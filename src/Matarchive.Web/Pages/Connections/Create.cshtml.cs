using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Matarchive.Web.Pages.Connections;

public sealed class CreateModel : ConnectionFormPageModel
{
    private readonly MatarchiveRepository _repository;

    public CreateModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    public void OnGet()
    {
        PrepareTypeOptions();
        ApplyTypeDefaults();
        OriginalType = Type;
        OriginalPort = Port;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        PrepareTypeOptions();
        ApplyTypeDefaults();

        if (!ValidateConnectionInput(requireIncomingSecret: true, requireOutgoingSecret: true))
        {
            return Page();
        }

        var connection = ToProfile();
        ConnectionTypeCatalog.ApplyDefaults(connection, OriginalType);
        connection.CreatedAt = DateTimeOffset.UtcNow;
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveConnectionAsync(connection);
        FlashSuccess = $"Verbindung {connection.Name} wurde angelegt.";
        return RedirectToPage("/Connections/Index");
    }
}
