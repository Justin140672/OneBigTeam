using System.Security.Claims;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeWorkloadActionProvider(string actionCategory, params WorkloadAction[] actions) : IWorkloadActionProvider
{
    public string ActionCategory => actionCategory;

    public Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkloadAction>>(actions);
}
