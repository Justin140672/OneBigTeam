using FluentValidation;

namespace HR.Modules.Employees.Features.GetMyEqualityData;

internal sealed class GetMyEqualityDataValidator : AbstractValidator<GetMyEqualityDataRequest>
{
    public GetMyEqualityDataValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
