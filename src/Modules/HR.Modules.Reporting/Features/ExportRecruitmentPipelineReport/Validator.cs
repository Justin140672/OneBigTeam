using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;

internal sealed class ExportRecruitmentPipelineReportValidator : AbstractValidator<ExportRecruitmentPipelineReportRequest>
{
    public ExportRecruitmentPipelineReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
