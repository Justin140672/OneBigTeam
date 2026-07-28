using FluentValidation;

namespace HR.Modules.Identity.Features.GetUserAuditHistory;

internal sealed class GetUserAuditHistoryValidator : AbstractValidator<GetUserAuditHistoryRequest>
{
    public GetUserAuditHistoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
