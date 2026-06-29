using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetProbationRecordByEmployee;

internal sealed class GetProbationRecordByEmployeeHandler
{
    private readonly ProbationDbContext _dbContext;

    public GetProbationRecordByEmployeeHandler(ProbationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetProbationRecordByEmployeeResponse>> HandleAsync(
        GetProbationRecordByEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProbationRecords
            .Where(r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return Result.Failure<GetProbationRecordByEmployeeResponse>(
                Error.NotFound("No probation record found for this employee."));
        }

        return Result.Success(new GetProbationRecordByEmployeeResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.ManagerEmployeeId,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString(),
            record.Notes,
            record.ExtensionReason,
            record.DecisionDate,
            record.DecisionMakerEmployeeId,
            record.OutcomeNotes,
            record.CreatedAt,
            record.UpdatedAt));
    }
}
