using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateRecruitmentStage;

internal sealed class UpdateRecruitmentStageHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<UpdateRecruitmentStageResponse>> HandleAsync(
        UpdateRecruitmentStageRequest request,
        CancellationToken cancellationToken)
    {
        var stage = await db.RecruitmentStages
            .SingleOrDefaultAsync(
                s => s.Id == request.RecruitmentStageId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (stage is null)
            return Result.Failure<UpdateRecruitmentStageResponse>(
                Error.NotFound($"Recruitment stage '{request.RecruitmentStageId}' was not found."));

        var trimmedName = request.Name.Trim();

        var duplicateName = await db.RecruitmentStages
            .AnyAsync(
                s => s.CompanyId == request.CompanyId && s.Id != request.RecruitmentStageId && s.Name == trimmedName,
                cancellationToken);

        if (duplicateName)
            return Result.Failure<UpdateRecruitmentStageResponse>(
                Error.Validation($"A recruitment stage named '{trimmedName}' already exists."));

        if (request.IsTerminal && request.TerminalOutcome != RecruitmentStageTerminalOutcome.None && stage.IsActive)
        {
            var duplicateTerminalOutcome = await db.RecruitmentStages
                .AnyAsync(
                    s => s.CompanyId == request.CompanyId
                        && s.Id != request.RecruitmentStageId
                        && s.IsActive
                        && s.TerminalOutcome == request.TerminalOutcome,
                    cancellationToken);

            if (duplicateTerminalOutcome)
                return Result.Failure<UpdateRecruitmentStageResponse>(
                    Error.Validation($"An active recruitment stage with terminal outcome '{request.TerminalOutcome}' already exists."));
        }

        // Guardrail: a stage flagged terminal=Hired/Rejected that this application-referencing stage
        // is currently the *only* active stage for that outcome must not be silently un-terminalled
        // if applications rely on it — enforced instead at deactivation time (see
        // SetRecruitmentStageActiveStatusHandler), since editing name/terminal flags without
        // deactivating does not orphan any existing Application.CurrentStageId reference.
        var before = new RecruitmentStageAuditSnapshot(stage.Name, stage.IsTerminal, stage.TerminalOutcome, stage.Purpose);

        var now = clock.UtcNowOffset();
        stage.UpdateDetails(trimmedName, request.IsTerminal, request.TerminalOutcome, now, request.Purpose);
        await db.SaveChangesAsync(cancellationToken);

        var after = new RecruitmentStageAuditSnapshot(stage.Name, stage.IsTerminal, stage.TerminalOutcome, stage.Purpose);

        await auditPublisher.PublishAsync(
            new RecruitmentStageUpdatedAuditEvent(stage.CompanyId, stage.Id, before, after, now),
            cancellationToken);

        return Result.Success(new UpdateRecruitmentStageResponse(
            stage.Id,
            stage.CompanyId,
            stage.Name,
            stage.DisplayOrder,
            stage.IsActive,
            stage.IsTerminal,
            stage.TerminalOutcome,
            stage.Purpose,
            stage.UpdatedAt));
    }
}
