using FluentValidation;

namespace ProxyManager.Application.ApiKeys;

public sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A name is required.")
            .MaximumLength(100).WithMessage("Name must be 100 characters or fewer.");
    }
}
