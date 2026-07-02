using FluentValidation;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class RecordMySicknessValidator : AbstractValidator<RecordMySicknessRequest>
{
    public RecordMySicknessValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.StartDate).NotEqual(default(DateOnly));
        RuleFor(r => r.StartDayPart).IsInEnum();
    }
}
