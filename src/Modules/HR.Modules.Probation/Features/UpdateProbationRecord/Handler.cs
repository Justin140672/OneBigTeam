using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed class UpdateProbationRecordHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateProbationRecordHandler(ProbationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
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

        record.Update(
            request.ManagerEmployeeId,
            request.ExpectedEndDate,
            status,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            string.IsNullOrWhiteSpace(request.ExtensionReason) ? null : request.ExtensionReason.Trim(),
            request.DecisionMakerEmployeeId,
            request.DecisionDate,
            string.IsNullOrWhiteSpace(request.OutcomeNotes) ? null : request.OutcomeNotes.Trim(),
            _clock.UtcNowOffset());

        await _dbContext.SaveChangesAsync(cancellationToken);

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
