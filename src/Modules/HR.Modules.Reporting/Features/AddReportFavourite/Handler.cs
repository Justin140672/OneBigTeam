using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.AddReportFavourite;

internal sealed class AddReportFavouriteHandler(ReportingDbContext dbContext, IClock clock)
{
    public async Task<Result<AddReportFavouriteResponse>> HandleAsync(
        AddReportFavouriteRequest request,
        Guid userId,
        ReportAccessGates accessGates,
        CancellationToken cancellationToken)
    {
        // REP-03: a report must exist in the registered catalogue, and the caller must currently be
        // authorized for it, before it can be favourited.
        if (!ReportCatalog.TryGet(request.ReportId, out var definition))
            return Result.Failure<AddReportFavouriteResponse>(
                Error.Validation($"'{request.ReportId}' is not a recognised report."));

        if (!accessGates.IsAuthorized(definition.AccessGate))
            return Result.Failure<AddReportFavouriteResponse>(
                Error.Forbidden($"You do not have access to report '{request.ReportId}'."));

        // Idempotent — favouriting an already-favourited report is a no-op success, not a conflict.
        var exists = await dbContext.ReportFavourites
            .AsNoTracking()
            .AnyAsync(f => f.CompanyId == request.CompanyId && f.UserId == userId && f.ReportId == request.ReportId,
                cancellationToken);

        if (!exists)
        {
            dbContext.ReportFavourites.Add(ReportFavourite.Create(
                Guid.NewGuid(), request.CompanyId, userId, request.ReportId, clock.UtcNowOffset()));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new AddReportFavouriteResponse(request.ReportId));
    }
}
