using FluentValidation;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed class SubmitSupportRequestValidator : AbstractValidator<SubmitSupportRequestRequest>
{
    public SubmitSupportRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.Priority).IsInEnum();
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).NotEmpty().MaximumLength(4000);
    }
}
