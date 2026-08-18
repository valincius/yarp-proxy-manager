using Microsoft.AspNetCore.DataProtection;
using ProxyManager.Application.Certificates;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("ProxyManager.Secrets");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
