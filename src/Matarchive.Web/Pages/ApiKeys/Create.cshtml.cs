using System.ComponentModel.DataAnnotations;
using Matarchive.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Matarchive.Web.Pages.ApiKeys;

public sealed class CreateModel : AppPageModel
{
    private readonly ApiKeyService _apiKeyService;

    public CreateModel(ApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    [TempData]
    public string? SecretValue { get; set; }

    [TempData]
    public string? SecretLabel { get; set; }

    [BindProperty]
    [Required]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var issued = await _apiKeyService.CreateAsync(Name.Trim());
        SecretLabel = issued.Record.Name;
        SecretValue = issued.Secret;
        FlashSuccess = $"API-Key {issued.Record.Name} wurde erstellt.";
        return RedirectToPage();
    }
}

