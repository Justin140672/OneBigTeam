using FluentValidation;

namespace HR.Modules.Reporting.Features.GetComplianceCentre;

internal sealed class GetComplianceCentreValidator : AbstractValidator<GetComplianceCentreRequest>
{
    public GetComplianceCentreValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();

        RuleFor(x => x.Category)
            .Must(v => v is null || Enum.TryParse<ComplianceCategory>(v, ignoreCase: true, out _))
            .WithMessage("Category must be a valid compliance category.");

        RuleFor(x => x.Severity)
            .Must(v => v is null || Enum.TryParse<ComplianceSeverity>(v, ignoreCase: true, out _))
            .WithMessage("Severity must be one of: Overdue, DueSoon, Informational.");

        RuleFor(x => x.DueDateEnd)
            .GreaterThanOrEqualTo(x => x.DueDateStart!.Value)
            .When(x => x.DueDateStart is not null && x.DueDateEnd is not null)
            .WithMessage("DueDateEnd must be on or after DueDateStart.");
    }
}
