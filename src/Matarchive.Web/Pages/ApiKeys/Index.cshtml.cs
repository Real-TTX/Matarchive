using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.ApiKeys;

public sealed class IndexModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public IndexModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<ApiKeyListItemViewModel> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var keys = await _repository.GetApiKeysAsync();
        Items = keys
            .OrderByDescending(key => key.CreatedAt)
            .Select(key => new ApiKeyListItemViewModel(
                key.Id,
                key.Name,
                key.Prefix,
                key.IsActive,
                key.CreatedAt,
                key.LastUsedAt))
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var key = await _repository.GetApiKeyByIdAsync(id);
        if (key is null)
        {
            FlashError = "Der API-Key wurde nicht gefunden.";
            return RedirectToPage();
        }

        await _repository.DeleteApiKeyAsync(id);
        FlashSuccess = $"API-Key {key.Name} wurde widerrufen.";
        return RedirectToPage();
    }
}

public sealed record ApiKeyListItemViewModel(
    Guid Id,
    string Name,
    string Prefix,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

