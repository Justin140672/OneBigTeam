using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.ListImportSessions;

internal sealed class ListImportSessionsHandler(DataImportDbContext db)
{
    public async Task<List<ImportSessionSummary>> HandleAsync(
        ListImportSessionsRequest request,
        CancellationToken cancellationToken)
    {
        return await db.ImportSessions
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ImportSessionSummary(
                s.Id,
                s.FileName,
                s.Status.ToString(),
                s.TotalRows,
                s.SuccessfulRows,
                s.FailedRows,
                s.CreatedAt,
                s.CompletedAt))
            .ToListAsync(cancellationToken);
    }
}
