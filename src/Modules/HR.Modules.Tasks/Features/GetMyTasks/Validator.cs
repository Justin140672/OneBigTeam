using FluentValidation;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class GetMyTasksValidator : AbstractValidator<GetMyTasksRequest>
{
    public GetMyTasksValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 200);
    }
}
