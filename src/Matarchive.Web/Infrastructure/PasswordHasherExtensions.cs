using Matarchive.Web.Domain;
using Microsoft.AspNetCore.Identity;

namespace Matarchive.Web.Infrastructure;

public sealed class PasswordHasherExtensions : IPasswordHasher<AppUser>
{
    private readonly PasswordHasher<AppUser> _inner = new();

    public string HashPassword(AppUser user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
        => _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
}

