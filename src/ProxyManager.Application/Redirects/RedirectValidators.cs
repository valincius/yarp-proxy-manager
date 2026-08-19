using FluentValidation;
using ProxyManager.Application;

namespace ProxyManager.Application.Redirects;

public sealed class RedirectHostValidator : AbstractValidator<RedirectHostInput>
{
    public RedirectHostValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DomainNames).NotNull().NotEmpty().WithMessage("At least one domain is required.");
        RuleFor(x => x.DomainNames)
            .Must(domains => domains.All(static d => DomainName.IsValid(d)))
            .WithMessage("One or more domains are invalid.");
        RuleFor(x => x.DomainNames)
            .Must(static domains => domains.Select(static d => DomainName.Normalize(d)).Distinct().Count() == domains.Count)
            .WithMessage("Duplicate domains are not allowed within one redirect.");
        RuleFor(x => x.StatusCode).Must(static s => s is 301 or 302).WithMessage("StatusCode must be 301 or 302.");
        RuleFor(x => x.ForwardScheme).Must(static s => s is "http" or "https").WithMessage("Forward scheme must be 'http' or 'https'.");
        RuleFor(x => x.ForwardHost).NotEmpty().MaximumLength(253);
        RuleFor(x => x.ForwardPort).InclusiveBetween(1, 65535);
    }
}

public sealed class AccessListValidator : AbstractValidator<AccessListInput>
{
    public AccessListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.Action).Must(static a => a is "Allow" or "Deny").WithMessage("Action must be 'Allow' or 'Deny'.");
            rule.RuleFor(r => r.Pattern)
                .NotEmpty()
                .Must(static p => p == "*" || IpPattern.IsValid(p))
                .WithMessage("Pattern must be an IP address, CIDR block, or '*'.");
        });
    }
}

/// <summary>Validates IP/CIDR patterns without pulling in an IP-network library.</summary>
public static class IpPattern
{
    public static bool IsValid(string pattern)
    {
        var parts = pattern.Split('/');
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(parts[0], out _))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 128)
            {
                return false;
            }

            // IPv4 addresses can only carry IPv4-style prefixes.
            if (parts[0].Contains('.') && prefix > 32)
            {
                return false;
            }
        }

        return true;
    }
}
