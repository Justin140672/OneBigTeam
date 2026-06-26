using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetProbationReviews;

internal sealed class GetProbationReviewsHandler
{
    private readonly ProbationDbContext _dbContext;

    public GetProbationReviewsHandler(ProbationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetProbationReviewsResponse>> HandleAsync(
        GetProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var recordExists = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (!recordExists)
            return Result.Failure<GetProbationReviewsResponse>(
                Error.NotFound("Probation record not found."));

        var items = await _dbContext.ProbationReviews
            .Where(r => r.CompanyId == request.CompanyId && r.ProbationRecordId == request.ProbationRecordId)
            .OrderBy(r => r.DueDate)
            .Select(r => new ProbationReviewItem(
                r.Id,
                r.ProbationRecordId,
                r.ReviewType.ToString(),
                r.DueDate,
                r.Status.ToString(),
                r.CompletedAt,
                r.CompletedByEmployeeId,
                r.Outcome == null ? null : r.Outcome.ToString(),
                r.Notes))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetProbationReviewsResponse(items));
    }
}
