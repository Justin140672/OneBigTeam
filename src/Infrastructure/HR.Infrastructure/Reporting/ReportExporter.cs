using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HR.Infrastructure.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HR.Infrastructure.Reporting;

/// <summary>
/// Generic, report-agnostic implementation of <see cref="IReportExporter"/>. Renders any
/// tabular <see cref="ReportExportData"/> payload to CSV, Excel or PDF. Must never contain
/// report-specific logic — the Employee Directory report is only the first consumer.
/// </summary>
internal sealed class ReportExporter : IReportExporter
{
    public ReportExportFile Export(ReportExportFormat format, ReportExportData data)
    {
        return format switch
        {
            ReportExportFormat.Csv => ExportCsv(data),
            ReportExportFormat.Excel => ExportExcel(data),
            ReportExportFormat.Pdf => ExportPdf(data),
            _ => throw new NotSupportedException($"Export format '{format}' is not supported."),
        };
    }

    private static ReportExportFile ExportCsv(ReportExportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', data.ColumnHeaders.Select(h => EscapeCsvField(NeutralizeFormulaInjection(h)))));

        foreach (var row in data.Rows)
            builder.AppendLine(string.Join(',', row.Select(v => EscapeCsvField(NeutralizeFormulaInjection(v)))));

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return new ReportExportFile(bytes, "text/csv", $"{SafeFileName(data.ReportTitle)}.csv");
    }

    private static string EscapeCsvField(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>
    /// Neutralises spreadsheet-formula injection (CSV/Excel formula injection, aka CSV injection).
    /// Any value that a spreadsheet application would interpret as the start of a formula
    /// (=, +, -, @) is prefixed so it is opened as plain text rather than executed. Leading
    /// whitespace and control characters are stripped before detection so they cannot be used
    /// to bypass the check. Genuine numeric values (including negative numbers) are left
    /// untouched so they remain usable as numbers.
    /// </summary>
    private static string NeutralizeFormulaInjection(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var index = 0;
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || char.IsControl(value[index])))
            index++;

        if (index >= value.Length)
            return value;

        var firstSignificantChar = value[index];
        if (firstSignificantChar is not ('=' or '+' or '-' or '@'))
            return value;

        // Genuine numeric values (e.g. "-42", " 3.14") are safe and must remain usable as numbers.
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            return value;

        return "'" + value;
    }

    private static ReportExportFile ExportExcel(ReportExportData data)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Report");

        for (var column = 0; column < data.ColumnHeaders.Count; column++)
            SetTextCell(worksheet.Cell(1, column + 1), data.ColumnHeaders[column]);

        worksheet.Row(1).Style.Font.Bold = true;

        for (var rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
        {
            var row = data.Rows[rowIndex];
            for (var column = 0; column < row.Count; column++)
                SetTextCell(worksheet.Cell(rowIndex + 2, column + 1), row[column]);
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return new ReportExportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{SafeFileName(data.ReportTitle)}.xlsx");
    }

    private static ReportExportFile ExportPdf(ReportExportData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Header().Text(data.ReportTitle).SemiBold().FontSize(14);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in data.ColumnHeaders)
                            columns.RelativeColumn();
                    });

                    foreach (var header in data.ColumnHeaders)
                    {
                        table.Cell().Border(1).Padding(2).Text(header).SemiBold();
                    }

                    foreach (var row in data.Rows)
                    {
                        foreach (var cell in row)
                        {
                            table.Cell().Border(1).Padding(2).Text(cell ?? string.Empty);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        var bytes = document.GeneratePdf();

        return new ReportExportFile(bytes, "application/pdf", $"{SafeFileName(data.ReportTitle)}.pdf");
    }

    /// <summary>
    /// Writes an untrusted string value into an Excel cell, explicitly as text, after
    /// neutralising any spreadsheet-formula injection prefix. This prevents both formula
    /// execution and ClosedXML/Excel auto-detection turning the value into a number, date
    /// or formula.
    /// </summary>
    private static void SetTextCell(IXLCell cell, string? value)
    {
        var neutralized = NeutralizeFormulaInjection(value ?? string.Empty);
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(neutralized);
    }

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return cleaned.Replace(' ', '-').ToLower(CultureInfo.InvariantCulture);
    }
}
