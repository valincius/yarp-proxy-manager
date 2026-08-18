using FluentValidation;

namespace ProxyManager.Application.ProxyHosts;

public sealed class ProxyHostValidator : AbstractValidator<ProxyHostInput>
{
    public ProxyHostValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.DomainNames)
            .NotNull()
            .NotEmpty()
            .WithMessage("At least one domain is required.");

        RuleFor(x => x.DomainNames)
            .Must(domains => domains.All(static d => DomainName.IsValid(d)))
            .WithMessage("One or more domains are invalid. Use hostnames (app.example.com), wildcards (*.example.com) or IPv4 addresses.");

        RuleFor(x => x.DomainNames)
            .Must(static domains =>
                domains.Select(static d => DomainName.Normalize(d)).Distinct().Count() == domains.Count)
            .WithMessage("Duplicate domains are not allowed within one host.");

        RuleFor(x => x.Scheme)
            .Must(static s => s is "http" or "https")
            .WithMessage("Scheme must be 'http' or 'https'.");

        RuleFor(x => x.ForwardHost)
            .NotEmpty()
            .MaximumLength(253);

        RuleFor(x => x.ForwardPort)
            .InclusiveBetween(1, 65535);

        RuleForEach(x => x.RequestHeaders).SetValidator(new ProxyHeaderInputValidator());
        RuleForEach(x => x.ResponseHeaders).SetValidator(new ProxyHeaderInputValidator());

        RuleForEach(x => x.Locations).SetValidator(new ProxyLocationInputValidator());

        RuleFor(x => x.Locations)
            .Must(static locations => locations.Select(static l => l.PathPrefix.TrimEnd('/')).Distinct().Count() == locations.Count)
            .WithMessage("Location path prefixes must be unique within a host.");
    }

    private sealed class ProxyLocationInputValidator : AbstractValidator<ProxyLocationInput>
    {
        public ProxyLocationInputValidator()
        {
            RuleFor(x => x.PathPrefix)
                .NotEmpty()
                .Must(static p => p.StartsWith("/", StringComparison.Ordinal))
                .WithMessage("Location path prefix must start with '/'.")
                .Must(static p => !p.Contains('?'))
                .WithMessage("Location path prefix must not contain query strings.");

            RuleFor(x => x.Scheme)
                .Must(static s => s is "http" or "https")
                .WithMessage("Scheme must be 'http' or 'https'.");

            RuleFor(x => x.ForwardHost).NotEmpty().MaximumLength(253);
            RuleFor(x => x.ForwardPort).InclusiveBetween(1, 65535);
            RuleFor(x => x.Order).GreaterThanOrEqualTo(0);
        }
    }

    private sealed class ProxyHeaderInputValidator : AbstractValidator<ProxyHeaderInput>
    {
        public ProxyHeaderInputValidator()
        {
            RuleFor(x => x.Target)
                .Must(static t => t is "Request" or "Response")
                .WithMessage("Header target must be 'Request' or 'Response'.");

            RuleFor(x => x.Action)
                .Must(static a => a is "Set" or "Append" or "Remove")
                .WithMessage("Header action must be 'Set', 'Append' or 'Remove'.");

            RuleFor(x => x.Name)
                .NotEmpty()
                .Must(static n => !n.Contains('\r') && !n.Contains('\n') && !n.Contains(' '))
                .WithMessage("Header name must be a valid HTTP header name (no whitespace or line breaks).");

            RuleFor(x => x.Value)
                .Must(static v => !v.Contains('\r') && !v.Contains('\n'))
                .WithMessage("Header value must not contain line breaks.");
        }
    }
}
