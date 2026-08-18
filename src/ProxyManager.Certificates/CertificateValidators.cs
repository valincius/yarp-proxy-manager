using FluentValidation;
using ProxyManager.Application;
using ProxyManager.Application.Certificates;

namespace ProxyManager.Certificates;

public sealed class IssueCertificateValidator : AbstractValidator<IssueCertificateRequest>
{
    public IssueCertificateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Domains).NotNull().NotEmpty().WithMessage("At least one domain is required.");
        RuleFor(x => x.Domains)
            .Must(domains => domains.All(static d => DomainName.IsValid(d)))
            .WithMessage("One or more domains are invalid.");
        RuleFor(x => x.Domains)
            .Must(static domains => domains.Select(static d => DomainName.Normalize(d)).Distinct().Count() == domains.Count)
            .WithMessage("Duplicate domains are not allowed.");
        RuleFor(x => x.ChallengeType)
            .Must(static c => c is "Http01" or "Dns01")
            .WithMessage("ChallengeType must be 'Http01' or 'Dns01'.");
        RuleFor(x => x.DnsCredentialId)
            .NotNull()
            .When(x => x.ChallengeType == "Dns01")
            .WithMessage("A DNS credential is required for DNS-01 challenges.");
    }
}

public sealed class UploadCertificateValidator : AbstractValidator<UploadCertificateRequest>
{
    public UploadCertificateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Domains).NotNull().NotEmpty().WithMessage("At least one domain is required.");
        RuleFor(x => x.Domains)
            .Must(domains => domains.All(static d => DomainName.IsValid(d)))
            .WithMessage("One or more domains are invalid.");

        RuleFor(x => x)
            .Must(static x => x.PfxBase64 is not null || (x.CertificatePem is not null && x.PrivateKeyPem is not null))
            .WithMessage("Provide either a PFX (base64) or a certificate PEM with its private key PEM.");
    }
}

public sealed class DnsCredentialValidator : AbstractValidator<DnsCredentialInput>
{
    public DnsCredentialValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ApiToken).NotEmpty().WithMessage("The API token is required.");
    }
}

public sealed class AcmeSettingsValidator : AbstractValidator<AcmeSettingsDto>
{
    public AcmeSettingsValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("An ACME account email is required.")
            .EmailAddress();

        RuleFor(x => x.DirectoryUrl)
            .Must(static url => string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("Directory URL must be a valid absolute URL.");
    }
}
