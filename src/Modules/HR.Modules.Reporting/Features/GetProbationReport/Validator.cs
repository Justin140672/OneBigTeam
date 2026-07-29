using FluentValidation;

namespace HR.Modules.Reporting.Features.GetProbationReport;

internal sealed class GetProbationReportValidator : AbstractValidator<GetProbationReportRequest>
{
    public GetProbationReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
