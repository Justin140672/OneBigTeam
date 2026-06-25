using FluentValidation;

namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed class CreateProbationRecordValidator : AbstractValidator<CreateProbationRecordRequest>
{
    public CreateProbationRecordValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.ManagerEmployeeId).NotEmpty();
        RuleFor(r => r.StartDate).NotEmpty();
        RuleFor(r => r.ExpectedEndDate).NotEmpty();
        RuleFor(r => r.Notes).MaximumLength(2000).When(r => r.Notes is not null);
    }
}
