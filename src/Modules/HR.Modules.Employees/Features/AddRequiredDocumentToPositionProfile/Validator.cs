using FluentValidation;

namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed class AddRequiredDocumentValidator : AbstractValidator<AddRequiredDocumentRequest>
{
    public AddRequiredDocumentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PositionProfileId).NotEmpty();
        RuleFor(r => r.DocumentTypeId).NotEmpty();

        RuleFor(r => r.DueDaysAfterStart)
            .GreaterThanOrEqualTo(0)
            .When(r => r.DueDaysAfterStart.HasValue)
            .WithMessage("DueDaysAfterStart must be 0 or greater.");
    }
}
