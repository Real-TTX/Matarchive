using System.Security.Claims;
using Matarchive.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Matarchive.Web.Infrastructure;

public sealed class AuthService
{
    private readonly MatarchiveRepository _repository;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        MatarchiveRepository repository,
        IPasswordHasher<AppUser> passwordHasher,
        ILogger<AuthService> logger)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByUsernameAsync(username, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _repository.SaveUserAsync(user, cancellationToken);
        return user;
    }

    public async Task SignInAsync(HttpContext httpContext, AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, MatarchiveConstants.AdminRole)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

        _logger.LogInformation("User {UserName} signed in", user.Username);
    }

    public Task SignOutAsync(HttpContext httpContext)
    {
        return httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public string HashPassword(AppUser user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }
}
