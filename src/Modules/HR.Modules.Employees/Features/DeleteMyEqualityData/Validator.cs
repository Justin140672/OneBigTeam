using FluentValidation;

namespace HR.Modules.Employees.Features.DeleteMyEqualityData;

internal sealed class DeleteMyEqualityDataValidator : AbstractValidator<DeleteMyEqualityDataRequest>
{
    public DeleteMyEqualityDataValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
