using FluentValidation;
using ProxyManager.Application.Exceptions;
using ProxyManager.Domain;

namespace ProxyManager.Application.ApiKeys;

/// <summary>Use-cases for API keys: create/list/delete, and validating a presented key.</summary>
public sealed class ApiKeyService
{
    private readonly IApiKeyRepository _repository;
    private readonly CreateApiKeyValidator _validator;
    private readonly TimeProvider _time;

    public ApiKeyService(
        IApiKeyRepository repository,
        CreateApiKeyValidator validator,
        TimeProvider? time = null)
    {
        _repository = repository;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var keys = await _repository.ListAsync(cancellationToken);
        return keys.Select(ToDto).ToList();
    }

    public async Task<CreatedApiKeyDto> CreateAsync(
        CreateApiKeyRequest request,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var plaintext = ApiKeyHasher.Generate();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            KeyHash = ApiKeyHasher.Hash(plaintext, GenerateSalt()),
            Prefix = plaintext[..Math.Min(10, plaintext.Length)],
            Enabled = true,
            CreatedBy = createdBy,
            CreatedAt = _time.GetUtcNow(),
        };

        await _repository.AddAsync(apiKey, cancellationToken);
        return new CreatedApiKeyDto(ToDto(apiKey), plaintext);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await _repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"API key '{id}' was not found.");

        await _repository.DeleteAsync(apiKey, cancellationToken);
    }

    /// <summary>
    /// Looks up a key by its prefix, verifies the hash, and marks it as used.
    /// Returns the key when valid and enabled, otherwise null.
    /// </summary>
    public async Task<ApiKey?> ValidateAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        var prefix = plaintext[..Math.Min(10, plaintext.Length)];
        var apiKey = await _repository.GetByPrefixAsync(prefix, cancellationToken);
        if (apiKey is null || !apiKey.Enabled || !ApiKeyHasher.Verify(plaintext, apiKey.KeyHash))
        {
            return null;
        }

        apiKey.LastUsedAt = _time.GetUtcNow();
        await _repository.TouchAsync(apiKey, cancellationToken);
        return apiKey;
    }

    private static byte[] GenerateSalt()
    {
        var salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static ApiKeyDto ToDto(ApiKey apiKey) => new(
        apiKey.Id,
        apiKey.Name,
        apiKey.Prefix,
        apiKey.Enabled,
        apiKey.CreatedAt,
        apiKey.LastUsedAt);
}
