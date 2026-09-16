using System.Reflection;
using HR.Modules.Tasks.Contracts;

namespace HR.Architecture.Tests;

/// <summary>
/// Ticket 15 (P1): every registered <see cref="ITaskCompletionAction"/> must be safe to replay —
/// see <see cref="ITaskCompletionAction"/>'s XML doc for the full contract
/// (TaskCompletionReconciliationJob calls <c>ExecuteAsync</c> again, with the SAME
/// <see cref="TaskCompletionContext.DispatchOperationId"/>, for any operation left Pending by an
/// interrupted dispatch).
///
/// This test enforces an explicit, reviewed inventory of every concrete
/// <see cref="ITaskCompletionAction"/> implementation across every module assembly loaded by this
/// test project. Adding a new action type that is NOT in <see cref="ExpectedActionTypeNames"/>
/// fails this test — forcing a deliberate decision (and, per the interface's documented contract, a
/// replay-safety review and a corresponding replay/recovery test in that module's own test project)
/// before the new action can be considered done, rather than silently shipping a fourth "already
/// resolved -> return Success with no recovery" bug alongside the ones ticket 15 fixed.
///
/// This is a structural inventory check, not a behavioural one — the actual replay behaviour for
/// each action is exercised by that module's own unit tests (e.g.
/// CompleteOnboardingTaskFromTaskActionTests, CompleteOffboardingTaskFromTaskActionTests,
/// CompleteProbationReviewFromTaskActionTests, SicknessEvidenceUploadCompletionAction's tests,
/// InterviewFeedbackServiceTests, AssetReturnService's tests).
/// </summary>
public class TaskCompletionActionReplaySafetyConformanceTests
{
    /// <summary>
    /// Every concrete <see cref="ITaskCompletionAction"/> implementation known to be replay-safe per
    /// ticket 15. Update this list (and add a replay/recovery test in the new action's own module
    /// test project) whenever a new action is registered.
    /// </summary>
    private static readonly HashSet<string> ExpectedActionTypeNames =
    [
        "HR.Modules.Tasks.Features.CompleteTask.Actions.LeaveTaskCompletionAction",
        "HR.Modules.Tasks.Features.CompleteTask.Actions.AssetTaskCompletionAction",
        "HR.Modules.Tasks.Features.CompleteTask.Actions.InterviewFeedbackTaskCompletionAction",
        "HR.Modules.Tasks.Features.CompleteTask.Actions.AssetReturnTaskCompletionAction",
        "HR.Modules.Tasks.Features.CompleteTask.Actions.ProbationTaskCompletionAction",
        "HR.Modules.Onboarding.Features.CompleteOnboardingTaskFromTask.CompleteOnboardingTaskFromTaskAction",
        "HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask.CompleteOffboardingTaskFromTaskAction",
        "HR.Modules.Probation.Features.CompleteProbationReviewFromTask.CompleteProbationReviewFromTaskAction",
        "HR.Modules.Sickness.Features.FulfilEvidenceRequest.SicknessEvidenceUploadCompletionAction",
        "HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask.CompleteReturnToWorkReviewFromTaskAction",
    ];

    /// <summary>
    /// Assemblies known to register <see cref="ITaskCompletionAction"/> implementations. Referenced
    /// explicitly (rather than scanning every loaded assembly) so this test's inventory is exactly as
    /// deliberate as <see cref="ExpectedActionTypeNames"/> itself.
    /// </summary>
    private static readonly Type[] ModuleMarkerTypes =
    [
        typeof(HR.Modules.Tasks.TasksModule),
        typeof(HR.Modules.Onboarding.OnboardingModule),
        typeof(HR.Modules.Offboarding.OffboardingModule),
        typeof(HR.Modules.Probation.ProbationModule),
        typeof(HR.Modules.Sickness.SicknessModule),
    ];

    [Fact]
    public void Every_Registered_ITaskCompletionAction_Is_In_The_Reviewed_Replay_Safety_Inventory()
    {
        var discovered = ModuleMarkerTypes
            .Select(t => t.Assembly)
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(ITaskCompletionAction).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToHashSet();

        var missingFromInventory = discovered.Except(ExpectedActionTypeNames).ToList();
        var noLongerRegistered = ExpectedActionTypeNames.Except(discovered).ToList();

        Assert.True(missingFromInventory.Count == 0,
            "New ITaskCompletionAction implementation(s) found that are not yet reviewed for " +
            "ticket 15 replay-safety: " + string.Join(", ", missingFromInventory) +
            ". Add a replay/recovery test in that module's test project, then add the type to " +
            $"{nameof(ExpectedActionTypeNames)} in this test.");

        Assert.True(noLongerRegistered.Count == 0,
            "ITaskCompletionAction implementation(s) in the reviewed inventory are no longer " +
            "registered (removed or renamed) — remove from " +
            $"{nameof(ExpectedActionTypeNames)}: " + string.Join(", ", noLongerRegistered));
    }
}
