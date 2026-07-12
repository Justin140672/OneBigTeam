using FluentValidation;

namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed class RejectProfilePhotoValidator : AbstractValidator<RejectProfilePhotoRequest>
{
    public RejectProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
