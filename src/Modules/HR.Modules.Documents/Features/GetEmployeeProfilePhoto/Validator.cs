using FluentValidation;

namespace HR.Modules.Documents.Features.GetEmployeeProfilePhoto;

internal sealed class GetEmployeeProfilePhotoValidator : AbstractValidator<GetEmployeeProfilePhotoRequest>
{
    public GetEmployeeProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
