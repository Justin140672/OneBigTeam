using FluentValidation;

namespace HR.Modules.Identity.Features.GetPermissionHistory;

internal sealed class GetPermissionHistoryValidator : AbstractValidator<GetPermissionHistoryRequest>
{
    public GetPermissionHistoryValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .When(x => x.FromDate is not null && x.ToDate is not null)
            .WithMessage("ToDate must not be before FromDate.");
    }
}
