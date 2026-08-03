using FluentValidation;

namespace HR.Modules.Employees.Features.GetEmploymentTypeSplit;

internal sealed class GetEmploymentTypeSplitValidator : AbstractValidator<GetEmploymentTypeSplitRequest>
{
    public GetEmploymentTypeSplitValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
