using ClosedXML.Excel;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests;

public class CompensationImportTemplateBuilderTests
{
    [Fact]
    public void Build_Creates_Main_Sheet_And_Instructions_Sheet_With_Expected_Names()
    {
        var bytes = CompensationImportTemplateBuilder.Build([]);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.True(workbook.TryGetWorksheet(CompensationImportTemplateBuilder.SheetName, out _));
        Assert.True(workbook.TryGetWorksheet(CompensationImportTemplateBuilder.InstructionsSheetName, out _));
    }

    [Fact]
    public void Build_Writes_Expected_Header_Row()
    {
        var bytes = CompensationImportTemplateBuilder.Build([]);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        for (var col = 0; col < CompensationImportTemplateBuilder.Headers.Length; col++)
        {
            Assert.Equal(CompensationImportTemplateBuilder.Headers[col], sheet.Cell(1, col + 1).GetString());
        }
    }

    [Fact]
    public void Build_Populates_Reference_Columns_From_Rows()
    {
        var rows = new[]
        {
            new CompensationImportTemplateRow("EMP-001", "Alice Smith", 45000m, "Annual"),
            new CompensationImportTemplateRow("EMP-002", "Bob Jones", null, null)
        };

        var bytes = CompensationImportTemplateBuilder.Build(rows);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        Assert.Equal("EMP-001", sheet.Cell(2, 1).GetString());
        Assert.Equal("Alice Smith", sheet.Cell(2, 2).GetString());
        Assert.Equal(45000m, sheet.Cell(2, 3).GetValue<decimal>());
        Assert.Equal("Annual", sheet.Cell(2, 4).GetString());

        Assert.Equal("EMP-002", sheet.Cell(3, 1).GetString());
        Assert.Equal("Bob Jones", sheet.Cell(3, 2).GetString());
        Assert.True(sheet.Cell(3, 3).IsEmpty());
        Assert.True(sheet.Cell(3, 4).IsEmpty());

        // Entry columns (New Salary, Effective Date, Reason, Notes) are left blank for every row.
        for (var col = 5; col <= CompensationImportTemplateBuilder.Headers.Length; col++)
        {
            Assert.True(sheet.Cell(2, col).IsEmpty());
            Assert.True(sheet.Cell(3, col).IsEmpty());
        }
    }

    [Fact]
    public void Build_Locks_Reference_Columns_And_Unlocks_Entry_Columns()
    {
        var rows = new[] { new CompensationImportTemplateRow("EMP-001", "Alice Smith", 45000m, "Annual") };

        var bytes = CompensationImportTemplateBuilder.Build(rows);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        Assert.True(sheet.Protection.IsProtected);

        // Header row is locked in full (including the entry-column headers) so column titles
        // themselves can never be edited — only the entry columns' data rows are unlocked below.
        for (var col = 1; col <= CompensationImportTemplateBuilder.Headers.Length; col++)
            Assert.True(sheet.Cell(1, col).Style.Protection.Locked);

        // Data row: reference columns locked, entry columns unlocked.
        for (var col = 1; col <= 4; col++)
            Assert.True(sheet.Cell(2, col).Style.Protection.Locked);
        for (var col = 5; col <= CompensationImportTemplateBuilder.Headers.Length; col++)
            Assert.False(sheet.Cell(2, col).Style.Protection.Locked);
    }

    [Fact]
    public void Build_Instructions_Sheet_Contains_Example_Rows()
    {
        var bytes = CompensationImportTemplateBuilder.Build([]);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var instructionsSheet = workbook.Worksheet(CompensationImportTemplateBuilder.InstructionsSheetName);

        var usedCells = instructionsSheet.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("ACME-003", usedCells);
        Assert.Contains("Priya Sharma", usedCells);
    }
}
