using FluentValidation;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class GetRecentLeaveRequestsValidator : AbstractValidator<GetRecentLeaveRequestsRequest>
{
    public GetRecentLeaveRequestsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Take!.Value)
            .InclusiveBetween(1, 50)
            .When(r => r.Take.HasValue);
    }
}
