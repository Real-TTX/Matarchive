using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Users;

public sealed class EditModel : AppPageModel
{
    private readonly AuthService _authService;
    private readonly MatarchiveRepository _repository;

    public EditModel(AuthService authService, MatarchiveRepository repository)
    {
        _authService = authService;
        _repository = repository;
    }

    [BindProperty]
    public Guid Id { get; set; }

    [BindProperty]
    [Required]
    [Display(Name = "Benutzername")]
    public string Username { get; set; } = "";

    [BindProperty]
    [Required]
    [Display(Name = "Anzeigename")]
    public string DisplayName { get; set; } = "";

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "Passwort")]
    public string Password { get; set; } = "";

    [BindProperty]
    [Display(Name = "Aktiv")]
    public bool IsActive { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await _repository.GetUserByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        Id = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        IsActive = user.IsActive;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _repository.GetUserByIdAsync(Id);
        if (user is null)
        {
            return NotFound();
        }

        var duplicate = await _repository.GetUserByUsernameAsync(Username);
        if (duplicate is not null && duplicate.Id != Id)
        {
            ModelState.AddModelError(nameof(Username), "Ein Benutzer mit diesem Namen existiert bereits.");
            return Page();
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.Equals(currentUserId, Id.ToString(), StringComparison.OrdinalIgnoreCase) && !IsActive)
        {
            ModelState.AddModelError(string.Empty, "Du kannst deinen eigenen Benutzer nicht deaktivieren.");
            return Page();
        }

        var remainingActiveAdmins = await _repository.GetUsersAsync();
        var activeAdminCountAfterChange = remainingActiveAdmins.Count(candidate => candidate.IsActive && candidate.IsAdmin && candidate.Id != Id);
        if (user.IsAdmin && user.IsActive && !IsActive && activeAdminCountAfterChange == 0)
        {
            ModelState.AddModelError(string.Empty, "Es muss mindestens ein aktiver Admin-Benutzer bleiben.");
            return Page();
        }

        user.Username = Username.Trim();
        user.DisplayName = DisplayName.Trim();
        user.IsActive = IsActive;
        user.IsAdmin = true;
        if (!string.IsNullOrWhiteSpace(Password))
        {
            user.PasswordHash = _authService.HashPassword(user, Password);
        }
        user.CreatedAt = user.CreatedAt == default ? DateTimeOffset.UtcNow : user.CreatedAt;

        await _repository.SaveUserAsync(user);
        FlashSuccess = $"Benutzer {user.DisplayName} wurde gespeichert.";
        return RedirectToPage("/Users/Index");
    }
}
