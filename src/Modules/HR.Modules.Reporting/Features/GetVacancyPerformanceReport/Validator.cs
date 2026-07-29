using FluentValidation;

namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed class GetVacancyPerformanceReportValidator : AbstractValidator<GetVacancyPerformanceReportRequest>
{
    public GetVacancyPerformanceReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
