using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed class UpdateSupportRequestStatusValidator : AbstractValidator<UpdateSupportRequestStatusRequest>
{
    public UpdateSupportRequestStatusValidator()
    {
        // Ticket 15 (optimistic concurrency) — a loaded concurrency version is mandatory on this
        // protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Status).IsInEnum();
    }
}
