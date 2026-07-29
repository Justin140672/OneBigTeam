using FluentValidation;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

internal sealed class GetRecruitmentPipelineReportValidator : AbstractValidator<GetRecruitmentPipelineReportRequest>
{
    public GetRecruitmentPipelineReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
