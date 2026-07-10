using FluentValidation;

namespace HR.Modules.Employees.Features.GetNewHiresTrend;

internal sealed class GetNewHiresTrendValidator : AbstractValidator<GetNewHiresTrendRequest>
{
    public GetNewHiresTrendValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
