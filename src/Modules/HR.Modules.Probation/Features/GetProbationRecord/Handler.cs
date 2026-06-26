using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetProbationRecord;

internal sealed class GetProbationRecordHandler
{
    private readonly ProbationDbContext _dbContext;

    public GetProbationRecordHandler(ProbationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetProbationRecordResponse>> HandleAsync(
        GetProbationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.RecordId,
                cancellationToken);

        if (record is null)
            return Result.Failure<GetProbationRecordResponse>(
                Error.NotFound("Probation record not found."));

        return Result.Success(new GetProbationRecordResponse(
            record.Id,
            record.CompanyId,
            record.Status.ToString(),
            record.ExpectedEndDate,
            record.ExtensionReason,
            record.DecisionDate,
            record.DecisionMakerEmployeeId,
            record.OutcomeNotes));
    }
}
