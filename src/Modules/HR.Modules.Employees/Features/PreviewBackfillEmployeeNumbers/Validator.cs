using FluentValidation;

namespace HR.Modules.Employees.Features.PreviewBackfillEmployeeNumbers;

internal sealed class PreviewBackfillEmployeeNumbersValidator : AbstractValidator<PreviewBackfillEmployeeNumbersRequest>
{
    public PreviewBackfillEmployeeNumbersValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
