using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed class WithdrawApplicationHandler(RecruitmentDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<WithdrawApplicationResponse>> HandleAsync(
        WithdrawApplicationRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<WithdrawApplicationResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Validation("This application has already been withdrawn."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is { IsTerminal: true })
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Validation($"Cannot withdraw an application already on the terminal stage '{currentStage.Name}'."));

        var now = clock.UtcNowOffset();

        // Ticket #99 judgement call: withdrawal is candidate-initiated and orthogonal to the pipeline
        // — there is no "Withdrawn" RecruitmentStage. CurrentStageId is left unchanged (the stage the
        // application was at when withdrawn is preserved for historical accuracy); WithdrawnAt is set
        // instead so Kanban/reporting can treat this application as inactive. No stage-history entry
        // or ApplicantStageChangedIntegrationEvent is recorded here, since CurrentStageId does not
        // change — only the audit trail (below) records that the withdrawal happened.
        application.Withdraw(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new ApplicationWithdrawnAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                application.CurrentStageId,
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new WithdrawApplicationResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.WithdrawnAt,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
