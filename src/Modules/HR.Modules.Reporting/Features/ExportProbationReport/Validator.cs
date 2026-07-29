using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportProbationReport;

internal sealed class ExportProbationReportValidator : AbstractValidator<ExportProbationReportRequest>
{
    public ExportProbationReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
