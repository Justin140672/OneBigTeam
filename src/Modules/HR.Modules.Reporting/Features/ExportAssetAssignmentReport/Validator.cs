using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportAssetAssignmentReport;

internal sealed class ExportAssetAssignmentReportValidator : AbstractValidator<ExportAssetAssignmentReportRequest>
{
    public ExportAssetAssignmentReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
