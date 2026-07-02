using FluentValidation;

namespace HR.Modules.Sickness.Features.RecordSickness;

internal sealed class RecordSicknessValidator : AbstractValidator<RecordSicknessRequest>
{
    public RecordSicknessValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.StartDate).NotEqual(default(DateOnly));
        RuleFor(r => r.StartDayPart).IsInEnum();
    }
}
