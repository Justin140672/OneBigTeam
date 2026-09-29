using ClosedXML.Excel;

namespace HR.Modules.Employees.Services;

internal sealed record CompensationImportParsedRow(
    int RowNumber,
    string EmployeeNumber,
    string? NewSalary,
    string? SalaryFrequency,
    DateOnly? EffectiveDate,
    string? Reason,
    string? Notes);

internal static class CompensationImportFileParser
{
    public static IReadOnlyList<CompensationImportParsedRow> Parse(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheet(1);
        var usedRange = worksheet.RangeUsed();

        if (usedRange is null)
            return [];

        var lastColumn = usedRange.LastColumn().ColumnNumber();
        var lastRow = usedRange.LastRow().RowNumber();

        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = worksheet.Cell(1, col).GetString().Trim();
            if (header.Length > 0 && !headerIndex.ContainsKey(header))
                headerIndex[header] = col;
        }

        string? GetString(int row, string header)
        {
            if (!headerIndex.TryGetValue(header, out var col))
                return null;

            var value = worksheet.Cell(row, col).GetString().Trim();
            return value.Length > 0 ? value : null;
        }

        DateOnly? GetDate(int row, string header)
        {
            if (!headerIndex.TryGetValue(header, out var col))
                return null;

            var cell = worksheet.Cell(row, col);

            if (cell.TryGetValue(out DateTime dateTime))
                return DateOnly.FromDateTime(dateTime);

            var text = cell.GetString().Trim();
            return DateOnly.TryParse(text, out var parsed) ? parsed : null;
        }

        var rows = new List<CompensationImportParsedRow>();

        for (var row = 2; row <= lastRow; row++)
        {
            var employeeNumber = GetString(row, "Employee Number");
            var newSalary = GetString(row, "New Salary");
            var salaryFrequency = GetString(row, "Salary Frequency");
            var effectiveDate = GetDate(row, "Effective Date");
            var reason = GetString(row, "Reason");
            var notes = GetString(row, "Notes");

            var rowIsBlank = employeeNumber is null && newSalary is null && effectiveDate is null && reason is null;
            if (rowIsBlank)
                continue;

            rows.Add(new CompensationImportParsedRow(
                row, employeeNumber ?? string.Empty, newSalary, salaryFrequency, effectiveDate, reason, notes));
        }

        return rows;
    }
}
