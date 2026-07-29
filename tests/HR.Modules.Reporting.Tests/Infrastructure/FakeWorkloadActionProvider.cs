using System.Security.Claims;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IWorkloadActionProvider"/> used to exercise
/// GetWorkloadActionsHandler's merge/urgency/filter/group/summary logic without depending on any
/// real module's DbContext — the handler never re-derives per-provider authorization, so a fake
/// provider that simply returns a fixed set of actions is a faithful stand-in.
/// </summary>
internal sealed class FakeWorkloadActionProvider(string actionCategory, params WorkloadAction[] actions) : IWorkloadActionProvider
{
    public string ActionCategory => actionCategory;

    public Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkloadAction>>(actions);
}
