using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.GetImportSessionColumns;

internal sealed class GetImportSessionColumnsHandler(
    DataImportDbContext db,
    IImportFileStorageService storage,
    EmployeeImportFileParser parser)
{
    public async Task<Result<GetImportSessionColumnsResponse>> HandleAsync(
        GetImportSessionColumnsRequest request,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<GetImportSessionColumnsResponse>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        IReadOnlyList<string> detectedHeaders;

        try
        {
            await using var fileStream = await storage.OpenReadAsync(session.StorageKey, cancellationToken);
            detectedHeaders = parser.ParseHeaders(fileStream, session.FileName);
        }
        catch (Exception ex)
        {
            return Result.Failure<GetImportSessionColumnsResponse>(
                Error.Validation($"The file could not be read: {ex.Message}"));
        }

        var suggestions = StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName
            .Select(kvp => new ImportFieldSuggestion(
                kvp.Key,
                kvp.Value,
                SuggestHeader(kvp.Value, detectedHeaders)))
            .OrderBy(s => s.TargetField)
            .ToList();

        return Result.Success(new GetImportSessionColumnsResponse(
            session.Id,
            detectedHeaders,
            suggestions));
    }

    private static string? SuggestHeader(string standardHeaderName, IReadOnlyList<string> detectedHeaders)
    {
        var exact = detectedHeaders.FirstOrDefault(
            h => string.Equals(h, standardHeaderName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var normalizedStandard = Normalize(standardHeaderName);
        return detectedHeaders.FirstOrDefault(h => Normalize(h) == normalizedStandard);
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
