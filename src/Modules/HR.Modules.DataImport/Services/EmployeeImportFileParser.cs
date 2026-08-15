using ClosedXML.Excel;

namespace HR.Modules.DataImport.Services;

/// <summary>
/// A single mapped data row from an import file. RowNumber matches the row as it appears
/// in the source file (the header is row 1, so the first data row is row 2).
/// </summary>
internal sealed record ParsedImportRow(int RowNumber, IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// The result of parsing an import file: the set of target fields that were actually found
/// (mapped) in the file's header row, plus every parsed data row.
/// </summary>
internal sealed record EmployeeImportParseResult(
    IReadOnlySet<string> MappedFields,
    IReadOnlyList<ParsedImportRow> Rows);

/// <summary>
/// Parses an employee import XLSX workbook into mapped rows using a column mapping profile.
/// A target field whose header isn't found in the file is simply absent from every row's field set.
/// </summary>
internal sealed class EmployeeImportFileParser
{
    public EmployeeImportParseResult Parse(Stream content, ColumnMappingProfile mapping)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheet(1);
        var usedRange = worksheet.RangeUsed();

        if (usedRange is null)
            return new EmployeeImportParseResult(new HashSet<string>(), []);

        var lastColumn = usedRange.LastColumn().ColumnNumber();
        var lastRow = usedRange.LastRow().RowNumber();

        var headerCells = new List<string>();
        for (var col = 1; col <= lastColumn; col++)
            headerCells.Add(worksheet.Cell(1, col).GetString());

        var columnIndexByTargetField = ResolveColumnIndexes(headerCells, mapping);

        var rows = new List<ParsedImportRow>();

        for (var row = 2; row <= lastRow; row++)
        {
            var rowIsEmpty = true;
            var cells = new List<string>();
            for (var col = 1; col <= lastColumn; col++)
            {
                var value = worksheet.Cell(row, col).GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    rowIsEmpty = false;
                cells.Add(value);
            }

            if (rowIsEmpty)
                continue;

            var fields = BuildFieldMap(columnIndexByTargetField, index => index < cells.Count ? cells[index] : null);
            rows.Add(new ParsedImportRow(row, fields));
        }

        return new EmployeeImportParseResult(columnIndexByTargetField.Keys.ToHashSet(), rows);
    }

    /// <summary>
    /// Reads just the header row of an import workbook, for column-mapping purposes.
    /// </summary>
    public IReadOnlyList<string> ParseHeaders(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheet(1);
        var usedRange = worksheet.RangeUsed();

        if (usedRange is null)
            return [];

        var lastColumn = usedRange.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var col = 1; col <= lastColumn; col++)
        {
            var value = worksheet.Cell(1, col).GetString().Trim();
            if (value.Length > 0)
                headers.Add(value);
        }

        return headers;
    }

    private static Dictionary<string, int> ResolveColumnIndexes(
        IReadOnlyList<string> headerCells,
        ColumnMappingProfile mapping)
    {
        var result = new Dictionary<string, int>();

        foreach (var (targetField, headerName) in mapping.TargetFieldToHeaderName)
        {
            for (var i = 0; i < headerCells.Count; i++)
            {
                if (string.Equals(headerCells[i].Trim(), headerName, StringComparison.OrdinalIgnoreCase))
                {
                    result[targetField] = i;
                    break;
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string?> BuildFieldMap(
        Dictionary<string, int> columnIndexByTargetField,
        Func<int, string?> cellValueAt)
    {
        var fields = new Dictionary<string, string?>();

        foreach (var (targetField, columnIndex) in columnIndexByTargetField)
        {
            var raw = cellValueAt(columnIndex);
            var trimmed = raw?.Trim();
            fields[targetField] = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        return fields;
    }
}
