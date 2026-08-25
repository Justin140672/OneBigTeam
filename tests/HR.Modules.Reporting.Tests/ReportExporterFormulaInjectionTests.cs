using System.Text;
using ClosedXML.Excel;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Reporting;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// Covers REP-01: spreadsheet-formula-injection (CSV injection) neutralization in
/// <see cref="ReportExporter"/> for both CSV and Excel export paths.
/// </summary>
public class ReportExporterFormulaInjectionTests
{
    private readonly ReportExporter _sut = new();

    private static ReportExportData BuildData(
        string reportTitle,
        IReadOnlyList<string> columnHeaders,
        IReadOnlyList<IReadOnlyList<string?>> rows)
        => new(reportTitle, columnHeaders, rows);

    private static string DecodeCsv(ReportExportFile file)
        => Encoding.UTF8.GetString(file.Content);

    // 1. Malicious employee name in a row is neutralized in CSV export.
    [Fact]
    public void ExportCsv_MaliciousEmployeeName_IsNeutralizedWithLeadingApostrophe()
    {
        var data = BuildData(
            "Employee Directory",
            ["Name", "Department"],
            [["=cmd|' /C calc'!A0", "Engineering"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'=cmd|", DecodeCsv(file));
    }

    // 2. +, -, @ prefixes are all neutralized (except genuine numbers, covered separately).
    [Theory]
    [InlineData("+1+1")]
    [InlineData("-2+3+cmd|' /C calc'!A0")]
    [InlineData("@SUM(A1:A2)")]
    public void ExportCsv_FormulaPrefixedValues_AreNeutralized(string maliciousValue)
    {
        var data = BuildData("Report", ["Value"], [[maliciousValue]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'" + maliciousValue, DecodeCsv(file));
    }

    // 3. Genuine negative numbers must remain untouched (usable as numbers).
    [Theory]
    [InlineData("-42")]
    [InlineData("-3.14")]
    public void ExportCsv_GenuineNegativeNumbers_AreNotNeutralized(string numericValue)
    {
        var data = BuildData("Report", ["Value"], [[numericValue]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        var csv = DecodeCsv(file);
        Assert.Contains(numericValue, csv);
        Assert.DoesNotContain("'" + numericValue, csv);
    }

    // 4. Leading whitespace before a formula prefix must not bypass detection.
    [Fact]
    public void ExportCsv_LeadingWhitespaceBeforeFormula_IsStillNeutralized()
    {
        var data = BuildData("Report", ["Value"], [["   =cmd"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'   =cmd", DecodeCsv(file));
    }

    // 5. Leading control characters before a formula prefix must not bypass detection.
    [Fact]
    public void ExportCsv_LeadingControlCharacterBeforeFormula_IsStillNeutralized()
    {
        var data = BuildData("Report", ["Value"], [["\t=cmd"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'\t=cmd", DecodeCsv(file));
    }

    // 6. Existing CSV escaping (commas/quotes/newlines) must not regress.
    [Fact]
    public void ExportCsv_ValuesWithCommasAndQuotes_AreStillCsvEscaped()
    {
        var data = BuildData("Report", ["Name"], [["Smith, \"Bob\""]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("\"Smith, \"\"Bob\"\"\"", DecodeCsv(file));
    }

    // 7. Excel export: malicious department name is stored as literal text, not a formula.
    [Fact]
    public void ExportExcel_MaliciousDepartmentName_IsStoredAsTextNotFormula()
    {
        var data = BuildData(
            "Employee Directory",
            ["Name", "Department"],
            [["Alice", "=HYPERLINK(\"http://evil\")"]]);

        var file = _sut.Export(ReportExportFormat.Excel, data);

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var worksheet = workbook.Worksheets.First();
        var cell = worksheet.Cell(2, 2);

        // ClosedXML/Excel treat a leading apostrophe as a "force text" marker: it is consumed
        // to select the text data type and is not retained as part of the stored string. What
        // matters for injection protection is that the cell is stored as literal text (not a
        // formula) and, when opened, displays the formula-looking string rather than executing it.
        Assert.False(cell.HasFormula);
        Assert.True(string.IsNullOrEmpty(cell.FormulaA1));
        Assert.Equal("=HYPERLINK(\"http://evil\")", cell.GetString());
    }

    // 8. Excel export: column headers are also neutralized.
    [Fact]
    public void ExportExcel_MaliciousColumnHeader_IsNeutralized()
    {
        var data = BuildData(
            "Report",
            ["=cmd|' /C calc'!A0"],
            [["value"]]);

        var file = _sut.Export(ReportExportFormat.Excel, data);

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var worksheet = workbook.Worksheets.First();
        var headerCell = worksheet.Cell(1, 1);

        Assert.False(headerCell.HasFormula);
        Assert.Equal("=cmd|' /C calc'!A0", headerCell.GetString());
    }

    // 9a. Generic free-text value category.
    [Fact]
    public void ExportCsv_GenericFreeTextValue_IsNeutralized()
    {
        var data = BuildData("Report", ["Notes"], [["=1+1"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'=1+1", DecodeCsv(file));
    }

    // 9b. Report title / filename category: SafeFileName strips invalid filename characters
    // (it does not, and does not need to, neutralize formula prefixes). A filename is never
    // opened as a spreadsheet formula by a spreadsheet application — it is only ever used as
    // an OS-level file name — so "=" is not an injection vector there. This test documents
    // that the produced filename is a safe, valid OS file name for a malicious-looking title,
    // without asserting formula-neutralization of the filename itself (which is out of scope
    // and not a real vulnerability for REP-01).
    [Fact]
    public void ExportCsv_ReportTitleStartingWithFormulaPrefix_ProducesValidOsFileName()
    {
        var data = BuildData("=cmd|' /C calc'!A0", ["Value"], [["x"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        var invalidChars = Path.GetInvalidFileNameChars();
        Assert.All(file.FileName, c => Assert.DoesNotContain(c, invalidChars));
    }
}
