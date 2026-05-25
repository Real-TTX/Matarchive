using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Connections;

public sealed class IndexModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public IndexModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<ConnectionListItemViewModel> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var connections = await _repository.GetConnectionsAsync();
        var tasks = await _repository.GetTasksAsync();

        Items = connections
            .OrderBy(connection => connection.Name)
            .Select(connection =>
            {
                var usageCount = tasks.Count(task => task.SourceConnectionId == connection.Id || task.DestinationConnectionId == connection.Id);
                var secretHint = string.IsNullOrWhiteSpace(connection.Secret) ? "kein Passwort" : "Passwort gesetzt";
                var descriptor = ConnectionTypeCatalog.GetDescriptor(connection.Type);
                return new ConnectionListItemViewModel(
                    connection.Id,
                    connection.Name,
                    descriptor.DisplayName,
                    ConnectionTypeCatalog.GetSummary(connection),
                    descriptor.Description,
                    connection.Notes,
                    usageCount,
                    secretHint);
            })
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var connection = await _repository.GetConnectionByIdAsync(id);
        if (connection is null)
        {
            FlashError = "Die Verbindung wurde nicht gefunden.";
            return RedirectToPage();
        }

        var tasks = await _repository.GetTasksAsync();
        if (tasks.Any(task => task.SourceConnectionId == id || task.DestinationConnectionId == id))
        {
            FlashError = "Die Verbindung wird noch von mindestens einem Task verwendet.";
            return RedirectToPage();
        }

        await _repository.DeleteConnectionAsync(id);
        FlashSuccess = $"Verbindung {connection.Name} wurde gelöscht.";
        return RedirectToPage();
    }
}

public sealed record ConnectionListItemViewModel(
    Guid Id,
    string Name,
    string Type,
    string Summary,
    string Description,
    string Notes,
    int UsageCount,
    string SecretHint);
