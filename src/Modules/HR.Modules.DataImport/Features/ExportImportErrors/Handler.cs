using System.Text;
using HR.Modules.DataImport.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.ExportImportErrors;

internal sealed class ExportImportErrorsHandler(DataImportDbContext db)
{
    public async Task<Result<byte[]>> HandleAsync(
        ExportImportErrorsRequest request,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<byte[]>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        var errors = await db.ImportRowErrors
            .AsNoTracking()
            .Where(e => e.ImportSessionId == session.Id && e.CompanyId == request.CompanyId)
            .OrderBy(e => e.RowNumber)
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.Append("RowNumber,Severity,ErrorMessage,RawRowData").Append('\n');

        foreach (var error in errors)
        {
            csv.Append(error.RowNumber).Append(',')
                .Append(CsvEscape(error.Severity.ToString())).Append(',')
                .Append(CsvEscape(error.ErrorMessage)).Append(',')
                .Append(CsvEscape(error.RawRowData))
                .Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());

        return Result.Success(bytes);
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
