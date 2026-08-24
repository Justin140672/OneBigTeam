using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed class UpdateProbationRecordHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ProbationReviewRecalculationService _recalculationService;
    private readonly ICompanyProbationSettingsReader _probationSettingsReader;

    public UpdateProbationRecordHandler(
        ProbationDbContext dbContext,
        IClock clock,
        ProbationReviewRecalculationService recalculationService,
        ICompanyProbationSettingsReader probationSettingsReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _recalculationService = recalculationService;
        _probationSettingsReader = probationSettingsReader;
    }

    public async Task<Result<UpdateProbationRecordResponse>> HandleAsync(
        UpdateProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.Id,
                cancellationToken);

        if (record is null)
            return Result.Failure<UpdateProbationRecordResponse>(
                Error.NotFound("Probation record not found."));

        var status = Enum.Parse<ProbationStatus>(request.Status, ignoreCase: true);
        var previousExpectedEndDate = record.ExpectedEndDate;
        var now = _clock.UtcNowOffset();

        record.Update(
            request.ManagerEmployeeId,
            request.ExpectedEndDate,
            status,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            string.IsNullOrWhiteSpace(request.ExtensionReason) ? null : request.ExtensionReason.Trim(),
            request.DecisionMakerEmployeeId,
            request.DecisionDate,
            string.IsNullOrWhiteSpace(request.OutcomeNotes) ? null : request.OutcomeNotes.Trim(),
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // PROB-03: the expected end date directly drives the checkpoint/final-decision review
        // schedule, so any still-pending (not yet completed) reviews must be recalculated against
        // the new date. Gated on an actual change so a no-op update (e.g. only Notes changed)
        // never triggers redundant cancel-and-recreate churn — this is also what keeps repeated/
        // retried calls with an unchanged date idempotent.
        if (request.ExpectedEndDate != previousExpectedEndDate)
        {
            var checkpointDays = await _probationSettingsReader.GetCheckpointDaysAsync(request.CompanyId, cancellationToken);
            await _recalculationService.RecalculateAsync(record, checkpointDays, now, cancellationToken);
        }

        return Result.Success(new UpdateProbationRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.ManagerEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString(),
            record.Notes,
            record.ExtensionReason,
            record.DecisionMakerEmployeeId,
            record.DecisionDate,
            record.OutcomeNotes,
            record.UpdatedAt));
    }
}
