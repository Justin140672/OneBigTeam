using FluentValidation;

namespace HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;

internal sealed class GetEmployeeAcknowledgementHistoryValidator
    : AbstractValidator<GetEmployeeAcknowledgementHistoryRequest>
{
    public GetEmployeeAcknowledgementHistoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
