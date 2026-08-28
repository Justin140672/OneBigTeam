using FluentValidation;

namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

internal sealed class GetCompanyAuditLogValidator : AbstractValidator<GetCompanyAuditLogRequest>
{
    public GetCompanyAuditLogValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
        RuleFor(r => r.EventType)
            .MaximumLength(200)
            .When(r => r.EventType is not null);
        RuleFor(r => r.ToDate)
            .GreaterThanOrEqualTo(r => r.FromDate)
            .When(r => r.FromDate.HasValue && r.ToDate.HasValue)
            .WithMessage("ToDate must be on or after FromDate.");
    }
}
