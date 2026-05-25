using System.ComponentModel.DataAnnotations;
using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Users;

public sealed class CreateModel : AppPageModel
{
    private readonly AuthService _authService;
    private readonly MatarchiveRepository _repository;

    public CreateModel(AuthService authService, MatarchiveRepository repository)
    {
        _authService = authService;
        _repository = repository;
    }

    [BindProperty]
    [Required]
    [Display(Name = "Benutzername")]
    public string Username { get; set; } = "";

    [BindProperty]
    [Required]
    [Display(Name = "Anzeigename")]
    public string DisplayName { get; set; } = "";

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Passwort")]
    public string Password { get; set; } = "";

    [BindProperty]
    [Display(Name = "Aktiv")]
    public bool IsActive { get; set; } = true;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existing = await _repository.GetUserByUsernameAsync(Username);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(Username), "Ein Benutzer mit diesem Namen existiert bereits.");
            return Page();
        }

        var user = new AppUser
        {
            Username = Username.Trim(),
            DisplayName = DisplayName.Trim(),
            IsAdmin = true,
            IsActive = IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _authService.HashPassword(user, Password);

        await _repository.SaveUserAsync(user);
        FlashSuccess = $"Benutzer {user.DisplayName} wurde angelegt.";
        return RedirectToPage("/Users/Index");
    }
}

