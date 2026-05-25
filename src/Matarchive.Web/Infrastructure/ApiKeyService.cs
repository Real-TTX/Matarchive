using System.Security.Cryptography;
using System.Text;
using Matarchive.Web.Domain;
using Microsoft.Extensions.Options;

namespace Matarchive.Web.Infrastructure;

public sealed class ApiKeyService
{
    private readonly MatarchiveRepository _repository;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(MatarchiveRepository repository, ILogger<ApiKeyService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public string GenerateSecret()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        var token = Convert.ToBase64String(buffer)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return $"mk_{token}";
    }

    public string ComputeHash(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<IssuedApiKey> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var secret = GenerateSecret();
        var record = new ApiKeyRecord
        {
            Name = name.Trim(),
            Prefix = secret[..Math.Min(12, secret.Length)],
            Hash = ComputeHash(secret),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.SaveApiKeyAsync(record, cancellationToken);
        _logger.LogInformation("Created API key {Name}", record.Name);

        return new IssuedApiKey
        {
            Record = record,
            Secret = secret
        };
    }

    public async Task<ApiKeyRecord?> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        var secret = ExtractSecret(request);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var hash = ComputeHash(secret);
        var keys = await _repository.GetApiKeysAsync(cancellationToken);
        var record = keys.FirstOrDefault(key => key.IsActive && key.Hash == hash);
        if (record is null)
        {
            return null;
        }

        record.LastUsedAt = DateTimeOffset.UtcNow;
        await _repository.SaveApiKeyAsync(record, cancellationToken);
        return record;
    }

    public string? ExtractSecret(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Matarchive-Api-Key", out var headerValues))
        {
            return headerValues.FirstOrDefault();
        }

        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyValues))
        {
            return apiKeyValues.FirstOrDefault();
        }

        if (request.Headers.TryGetValue("Authorization", out var authValues))
        {
            var auth = authValues.FirstOrDefault();
            if (auth is null)
            {
                return null;
            }

            const string bearerPrefix = "Bearer ";
            return auth.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? auth[bearerPrefix.Length..].Trim()
                : auth;
        }

        return null;
    }
}

