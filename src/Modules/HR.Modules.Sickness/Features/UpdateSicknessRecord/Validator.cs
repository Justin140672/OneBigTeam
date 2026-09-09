using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed class UpdateSicknessRecordValidator : AbstractValidator<UpdateSicknessRecordRequest>
{
    public UpdateSicknessRecordValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.StartDate).NotEqual(default(DateOnly));
        RuleFor(r => r.StartDayPart).IsInEnum();
    }
}
