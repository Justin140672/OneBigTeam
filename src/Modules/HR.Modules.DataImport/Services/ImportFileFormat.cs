namespace HR.Modules.DataImport.Services;

/// <summary>
/// Shared helper for detecting an import file's format from its file name extension.
/// </summary>
internal static class ImportFileFormat
{
    public static bool IsXlsx(string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
}
