using FluentValidation;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed class GetRecruitmentPipelineSummaryReportValidator : AbstractValidator<GetRecruitmentPipelineSummaryReportRequest>
{
    public GetRecruitmentPipelineSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
