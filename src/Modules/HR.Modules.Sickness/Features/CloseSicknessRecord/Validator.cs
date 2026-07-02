using FluentValidation;

namespace HR.Modules.Sickness.Features.CloseSicknessRecord;

internal sealed class CloseSicknessRecordValidator : AbstractValidator<CloseSicknessRecordRequest>
{
    public CloseSicknessRecordValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.EndDate).NotEqual(default(DateOnly));
        RuleFor(r => r.EndDayPart).IsInEnum();
    }
}
