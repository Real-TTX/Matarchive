using System.ComponentModel.DataAnnotations;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel : AppPageModel
{
    private readonly AuthService _authService;

    public LoginModel(AuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    [Required]
    [Display(Name = "Benutzername")]
    public string Username { get; set; } = "";

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Passwort")]
    public string Password { get; set; } = "";

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _authService.ValidateCredentialsAsync(Username, Password);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Die Anmeldung ist fehlgeschlagen.");
            return Page();
        }

        await _authService.SignInAsync(HttpContext, user);
        FlashSuccess = $"Willkommen zurück, {user.DisplayName}.";
        return RedirectToPage("/Dashboard");
    }
}

