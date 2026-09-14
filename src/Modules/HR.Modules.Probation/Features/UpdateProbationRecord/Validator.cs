using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed class UpdateProbationRecordValidator : AbstractValidator<UpdateProbationRecordRequest>
{
    public UpdateProbationRecordValidator()
    {
        // Ticket 16 (optimistic concurrency) — a loaded concurrency version is mandatory on this
        // protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.ManagerEmployeeId).NotEmpty();
        RuleFor(r => r.ExpectedEndDate).NotEmpty();

        RuleFor(r => r.Notes).MaximumLength(2000).When(r => r.Notes is not null);
    }
}
