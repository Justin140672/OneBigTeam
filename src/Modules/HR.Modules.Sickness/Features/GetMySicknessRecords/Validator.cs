using FluentValidation;

namespace HR.Modules.Sickness.Features.GetMySicknessRecords;

internal sealed class GetMySicknessRecordsValidator : AbstractValidator<GetMySicknessRecordsRequest>
{
    public GetMySicknessRecordsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
