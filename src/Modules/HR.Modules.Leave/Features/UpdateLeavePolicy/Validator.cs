using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed class UpdateLeavePolicyValidator : AbstractValidator<UpdateLeavePolicyRequest>
{
    public UpdateLeavePolicyValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PolicyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.CarryOverDays)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(365);
    }
}
