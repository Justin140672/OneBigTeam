using FluentValidation;

namespace HR.Modules.Employees.Features.CommitBackfillEmployeeNumbers;

internal sealed class CommitBackfillEmployeeNumbersValidator : AbstractValidator<CommitBackfillEmployeeNumbersRequest>
{
    public CommitBackfillEmployeeNumbersValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
