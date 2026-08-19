using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;

internal sealed class ExportRecruitmentPipelineSummaryReportValidator : AbstractValidator<ExportRecruitmentPipelineSummaryReportRequest>
{
    public ExportRecruitmentPipelineSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
