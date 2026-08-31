using System.Security.Claims;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Flexible <see cref="IWorkloadActionProvider"/> fake for DSH-06 DashboardSummaryComposer tests.
/// Unlike <see cref="FakeWorkloadActionProvider"/> (fixed action list) this lets a test drive the
/// provider's behaviour: return a set, throw, or honour the composer's linked deadline token.
/// </summary>
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
        CancellationToken cancellationToken) => _behaviour(cancellationToken);

    public static ConfigurableWorkloadActionProvider Returning(
        string actionCategory, params WorkloadAction[] actions) =>
        new(actionCategory, _ => Task.FromResult<IReadOnlyList<WorkloadAction>>(actions));

    public static ConfigurableWorkloadActionProvider Throwing(
        string actionCategory, Exception exception) =>
        new(actionCategory, _ => throw exception);

    /// <summary>
    /// Cooperatively blocks until its token is cancelled, then surfaces the resulting
    /// <see cref="OperationCanceledException"/> — models a slow module that respects the composer's
    /// per-summary deadline.
    /// </summary>
    public static ConfigurableWorkloadActionProvider HonouringDeadline(string actionCategory) =>
        new(actionCategory, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Array.Empty<WorkloadAction>();
        });
}
