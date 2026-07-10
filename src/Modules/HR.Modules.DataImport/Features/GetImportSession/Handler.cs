using HR.Modules.DataImport.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.GetImportSession;

internal sealed class GetImportSessionHandler(DataImportDbContext db)
{
    public async Task<Result<GetImportSessionResponse>> HandleAsync(
        GetImportSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<GetImportSessionResponse>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        return Result.Success(new GetImportSessionResponse(
            session.Id,
            session.EntityType,
            session.FileName,
            session.Status.ToString(),
            session.TotalRows,
            session.ProcessedRows,
            session.SuccessfulRows,
            session.FailedRows,
            session.StartedAt,
            session.CompletedAt,
            session.ErrorSummary,
            session.CreatedAt,
            session.UpdatedAt));
    }
}
