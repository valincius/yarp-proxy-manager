namespace ProxyManager.Application.Redirects;

public sealed record RedirectHostInput(
    string Name,
    IReadOnlyList<string> DomainNames,
    bool Enabled,
    int StatusCode,
    bool PreservePath,
    string ForwardScheme,
    string ForwardHost,
    int ForwardPort,
    Guid? CertificateId);

public sealed record AccessListInput(
    string Name,
    bool SatisfyAny,
    IReadOnlyList<AccessListRuleInput> Rules);

public sealed record AccessListRuleInput(string Action, string Pattern);
