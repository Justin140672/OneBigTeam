using FluentValidation;

namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

internal sealed class GetEqualityDiversityReportValidator : AbstractValidator<GetEqualityDiversityReportRequest>
{
    public GetEqualityDiversityReportValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
