using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;

namespace HR.Modules.Employees.Tests.Infrastructure;

// Shared scripting behaviour for the 4 cross-module history replayer fakes used by
// BackfillEmployeeTimelineHandlerTests. Each fake, when invoked, writes the configured number of
// EmployeeTimelineEntry rows directly into the same EmployeesDbContext instance the handler under
// test uses — simulating the real replayer's effect of publishing an integration event whose
// handler (unmodified, in-module) writes via IEmployeeTimelineWriter into that same DbContext/DI
// scope. This lets RunCrossModuleSourceAsync's before/after count-delta logic be exercised
// faithfully without needing a real integration event bus in these unit tests.
internal sealed class ScriptedReplayerBehavior(
    EmployeesDbContext dbContext,
    EmployeeTimelineEventType eventType)
{
    public int EntriesToCreate { get; set; }
    public int ProcessedOverride { get; set; } = -1;
    public bool ShouldThrow { get; set; }

    public async Task<int> InvokeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (ShouldThrow)
            throw new InvalidOperationException($"Simulated failure for {eventType}");

        for (var i = 0; i < EntriesToCreate; i++)
        {
            dbContext.EmployeeTimelineEntries.Add(EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                companyId,
                Guid.NewGuid(),
                new DateOnly(2026, 1, 1),
                eventType,
                EmployeeTimelineCategory.Employment,
                "Backfilled",
                "Backfilled entry.",
                performedByUserId: null,
                "Test",
                sourceRecordId: null,
                EmployeeTimelineVisibility.AuthorisedInternal,
                DateTimeOffset.UtcNow,
                backfilledAt: DateTimeOffset.UtcNow));
        }

        if (EntriesToCreate > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return ProcessedOverride >= 0 ? ProcessedOverride : EntriesToCreate;
    }
}

internal sealed class FakeProbationHistoryReplayer(ScriptedReplayerBehavior behavior) : IProbationHistoryReplayer
{
    public Task<int> ReplayProbationPassedAsync(Guid companyId, CancellationToken cancellationToken) =>
        behavior.InvokeAsync(companyId, cancellationToken);
}

internal sealed class FakeOnboardingHistoryReplayer(ScriptedReplayerBehavior behavior) : IOnboardingHistoryReplayer
{
    public Task<int> ReplayOnboardingCompletedAsync(Guid companyId, CancellationToken cancellationToken) =>
        behavior.InvokeAsync(companyId, cancellationToken);
}

internal sealed class FakeSharedCompanyDocumentAcknowledgementHistoryReplayer(ScriptedReplayerBehavior behavior)
    : ISharedCompanyDocumentAcknowledgementHistoryReplayer
{
    public Task<int> ReplaySharedCompanyDocumentAcknowledgedAsync(Guid companyId, CancellationToken cancellationToken) =>
        behavior.InvokeAsync(companyId, cancellationToken);
}

internal sealed class FakeOffboardingHistoryReplayer(ScriptedReplayerBehavior behavior) : IOffboardingHistoryReplayer
{
    public Task<int> ReplayStartedOffboardingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        behavior.InvokeAsync(companyId, cancellationToken);
}
