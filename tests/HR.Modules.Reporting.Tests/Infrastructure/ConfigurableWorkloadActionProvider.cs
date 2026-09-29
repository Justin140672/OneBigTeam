using System.Security.Claims;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class ConfigurableWorkloadActionProvider : IWorkloadActionProvider
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<WorkloadAction>>> _behaviour;

    private ConfigurableWorkloadActionProvider(
        string actionCategory,
        Func<CancellationToken, Task<IReadOnlyList<WorkloadAction>>> behaviour)
    {
        ActionCategory = actionCategory;
        _behaviour = behaviour;
    }

    public string ActionCategory { get; }

    public Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken) => _behaviour(cancellationToken);

    public static ConfigurableWorkloadActionProvider Returning(
        string actionCategory, params WorkloadAction[] actions) =>
        new(actionCategory, _ => Task.FromResult<IReadOnlyList<WorkloadAction>>(actions));

    public static ConfigurableWorkloadActionProvider Throwing(
        string actionCategory, Exception exception) =>
        new(actionCategory, _ => throw exception);

    public static ConfigurableWorkloadActionProvider HonouringDeadline(string actionCategory) =>
        new(actionCategory, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Array.Empty<WorkloadAction>();
        });
}
