using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Leave.Features.UpdateLeaveType;

internal sealed class UpdateLeaveTypeValidator : AbstractValidator<UpdateLeaveTypeRequest>
{
    public UpdateLeaveTypeValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Code).NotEmpty().MaximumLength(20);
        RuleFor(r => r.DefaultEntitlementDays).GreaterThanOrEqualTo(0);
    }
}
