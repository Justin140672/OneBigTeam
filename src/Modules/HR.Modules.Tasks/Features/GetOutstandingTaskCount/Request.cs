using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.GetOutstandingTaskCount;

internal sealed record GetOutstandingTaskCountRequest
{
    public Guid CompanyId { get; init; }
    public TaskSource? Source { get; init; }
    public TaskActionType? ActionType { get; init; }
}
