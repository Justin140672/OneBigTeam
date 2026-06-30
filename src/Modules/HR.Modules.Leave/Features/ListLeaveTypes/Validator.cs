using FluentValidation;

namespace HR.Modules.Leave.Features.ListLeaveTypes;

internal sealed class ListLeaveTypesValidator : AbstractValidator<ListLeaveTypesRequest>
{
    public ListLeaveTypesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
