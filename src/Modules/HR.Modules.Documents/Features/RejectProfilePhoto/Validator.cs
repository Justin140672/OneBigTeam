using FluentValidation;

namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed class RejectProfilePhotoValidator : AbstractValidator<RejectProfilePhotoRequest>
{
    public RejectProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();

        // Ticket requirement: "Rejection must require a clear reason" — HR must record why a
        // submitted photo was rejected so the employee understands what to fix before resubmitting.
        RuleFor(r => r.RejectionReason)
            .NotEmpty()
            .WithMessage("A rejection reason is required.");
    }
}
