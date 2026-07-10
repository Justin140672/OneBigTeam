using FluentValidation;

namespace HR.Modules.Employees.Features.GetHeadcountSummary;

internal sealed class GetHeadcountSummaryValidator : AbstractValidator<GetHeadcountSummaryRequest>
{
    public GetHeadcountSummaryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
