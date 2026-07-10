namespace HR.Modules.DataImport.Features.GetImportSessionColumns;

internal sealed record ImportFieldSuggestion(
    string TargetField,
    string StandardHeaderName,
    string? SuggestedHeader);

internal sealed record GetImportSessionColumnsResponse(
    Guid ImportSessionId,
    IReadOnlyList<string> DetectedHeaders,
    IReadOnlyList<ImportFieldSuggestion> FieldSuggestions);
