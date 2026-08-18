namespace ProxyManager.Application.Certificates;

/// <summary>Protects and unprotects secrets at rest (Data Protection in Infrastructure).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
