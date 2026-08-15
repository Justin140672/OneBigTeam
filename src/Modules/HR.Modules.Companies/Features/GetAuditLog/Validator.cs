using FluentValidation;

namespace HR.Modules.Companies.Features.GetAuditLog;

internal sealed class GetAuditLogValidator : AbstractValidator<GetAuditLogRequest>
{
    public GetAuditLogValidator()
    {
        RuleFor(r => r.AdministratorEmail)
            .MaximumLength(320)
            .When(r => r.AdministratorEmail is not null);

        RuleFor(r => r.EventType)
            .Must(eventType => AuditLogActionTypes.All.Contains(eventType))
            .WithMessage("EventType must be one of the recognised platform audit action types.")
            .When(r => r.EventType is not null);

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(r => r)
            .Must(r => !r.FromDate.HasValue || !r.ToDate.HasValue || r.FromDate <= r.ToDate)
            .WithMessage("FromDate must not be after ToDate.")
            .WithName("FromDate");
    }
}
