using FluentValidation;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed class AddSupportResponseValidator : AbstractValidator<AddSupportResponseRequest>
{
    public AddSupportResponseValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.BodyHtml).NotEmpty().MaximumLength(8000);
    }
}
