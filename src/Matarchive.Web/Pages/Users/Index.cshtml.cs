using System.Security.Claims;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Users;

public sealed class IndexModel : AppPageModel
{
    private readonly MatarchiveRepository _repository;

    public IndexModel(MatarchiveRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<UserListItemViewModel> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var users = await _repository.GetUsersAsync();
        Items = users
            .OrderBy(user => user.Username)
            .Select(user => new UserListItemViewModel(
                user.Id,
                user.Username,
                user.DisplayName,
                user.IsActive,
                user.IsAdmin,
                user.LastLoginAt))
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var users = await _repository.GetUsersAsync();
        var user = users.FirstOrDefault(candidate => candidate.Id == id);
        if (user is null)
        {
            FlashError = "Der Benutzer wurde nicht gefunden.";
            return RedirectToPage();
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.Equals(currentUserId, id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            FlashError = "Du kannst deinen eigenen Benutzer nicht löschen.";
            return RedirectToPage();
        }

        var activeAdmins = users.Count(candidate => candidate.IsActive && candidate.IsAdmin && candidate.Id != id);
        if (user.IsAdmin && user.IsActive && activeAdmins == 0)
        {
            FlashError = "Es muss mindestens ein aktiver Admin-Benutzer bleiben.";
            return RedirectToPage();
        }

        await _repository.DeleteUserAsync(id);
        FlashSuccess = $"Benutzer {user.DisplayName} wurde gelöscht.";
        return RedirectToPage();
    }
}

public sealed record UserListItemViewModel(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsActive,
    bool IsAdmin,
    DateTimeOffset? LastLoginAt);
