using FluentValidation;

namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed class GetLeaveBalanceHistoryValidator : AbstractValidator<GetLeaveBalanceHistoryRequest>
{
    public GetLeaveBalanceHistoryValidator()
    {
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveTypeId).NotEmpty();
    }
}
