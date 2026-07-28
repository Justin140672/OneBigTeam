using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

// Ticket #97: this is the only supported "removal" path for a stage — deactivation only, never a
// hard delete (mirrors ExternalRecruiter.SetActiveStatus's rationale). Enforces the remaining
// business rules that only bite at deactivation time: at least one active stage must remain overall,
// and — if this stage is the company's only active Hired/Rejected terminal stage — it cannot be
// deactivated, since that would leave HireCandidate/RejectCandidate with nowhere to move applications.
// A stage still referenced by any Application.CurrentStageId can always be deactivated (deactivation
// never breaks that reference — the row is preserved, just hidden from new selection).
internal sealed class SetRecruitmentStageActiveStatusHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<SetRecruitmentStageActiveStatusResponse>> HandleAsync(
        SetRecruitmentStageActiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        var stage = await db.RecruitmentStages
            .SingleOrDefaultAsync(
                s => s.Id == request.RecruitmentStageId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (stage is null)
            return Result.Failure<SetRecruitmentStageActiveStatusResponse>(
                Error.NotFound($"Recruitment stage '{request.RecruitmentStageId}' was not found."));

        if (!request.IsActive && stage.IsActive)
        {
            var otherActiveStagesCount = await db.RecruitmentStages
                .CountAsync(s => s.CompanyId == request.CompanyId && s.IsActive && s.Id != stage.Id, cancellationToken);

            if (otherActiveStagesCount == 0)
                return Result.Failure<SetRecruitmentStageActiveStatusResponse>(
                    Error.Validation("At least one active recruitment stage must remain."));

            if (stage.IsTerminal && stage.TerminalOutcome != RecruitmentStageTerminalOutcome.None)
            {
                var otherActiveWithSameOutcome = await db.RecruitmentStages
                    .AnyAsync(
                        s => s.CompanyId == request.CompanyId
                            && s.IsActive
                            && s.Id != stage.Id
                            && s.TerminalOutcome == stage.TerminalOutcome,
                        cancellationToken);

                if (!otherActiveWithSameOutcome)
                    return Result.Failure<SetRecruitmentStageActiveStatusResponse>(
                        Error.Validation($"Cannot deactivate the only active recruitment stage with terminal outcome '{stage.TerminalOutcome}'."));
            }
        }

        var previousIsActive = stage.IsActive;
        var now = clock.UtcNowOffset();

        stage.SetActiveStatus(request.IsActive, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new RecruitmentStageActiveStatusChangedAuditEvent(
                stage.CompanyId, stage.Id, stage.Name, previousIsActive, stage.IsActive, now),
            cancellationToken);

        return Result.Success(new SetRecruitmentStageActiveStatusResponse(
            stage.Id,
            stage.CompanyId,
            stage.Name,
            stage.IsActive,
            stage.UpdatedAt));
    }
}
