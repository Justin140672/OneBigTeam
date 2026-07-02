using FluentValidation;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed class UpdateSicknessRecordValidator : AbstractValidator<UpdateSicknessRecordRequest>
{
    public UpdateSicknessRecordValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.StartDate).NotEqual(default(DateOnly));
        RuleFor(r => r.StartDayPart).IsInEnum();
    }
}
