using System.Text;
using ClosedXML.Excel;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Reporting;

namespace HR.Modules.Reporting.Tests;

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

        Assert.False(cell.HasFormula);
        Assert.True(string.IsNullOrEmpty(cell.FormulaA1));
        Assert.Equal("=HYPERLINK(\"http://evil\")", cell.GetString());
    }

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

    [Fact]
    public void ExportCsv_GenericFreeTextValue_IsNeutralized()
    {
        var data = BuildData("Report", ["Notes"], [["=1+1"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        Assert.Contains("'=1+1", DecodeCsv(file));
    }

    [Fact]
    public void ExportCsv_ReportTitleStartingWithFormulaPrefix_ProducesValidOsFileName()
    {
        var data = BuildData("=cmd|' /C calc'!A0", ["Value"], [["x"]]);

        var file = _sut.Export(ReportExportFormat.Csv, data);

        var invalidChars = Path.GetInvalidFileNameChars();
        Assert.All(file.FileName, c => Assert.DoesNotContain(c, invalidChars));
    }
}
