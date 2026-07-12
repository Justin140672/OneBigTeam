using FluentValidation;

namespace HR.Modules.Documents.Features.ApproveProfilePhoto;

internal sealed class ApproveProfilePhotoValidator : AbstractValidator<ApproveProfilePhotoRequest>
{
    public ApproveProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
