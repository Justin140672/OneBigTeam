using System.IO.Packaging;
using System.Xml.Linq;
using ClosedXML.Excel;
using HR.Modules.DataImport.Features.DownloadImportTemplate;
using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests;

public class DownloadImportTemplateHandlerTests
{
    [Fact]
    public void Handle_EmployeeImportSheet_Has_Header_Row_Containing_Every_Standard_Header()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Employee Import");

        var usedRange = sheet.RangeUsed();
        Assert.NotNull(usedRange);

        var lastColumn = usedRange!.LastColumn().ColumnNumber();
        var headerCells = new List<string>();
        for (var col = 1; col <= lastColumn; col++)
            headerCells.Add(sheet.Cell(1, col).GetString());

        foreach (var expectedHeader in StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Values)
        {
            Assert.Contains(expectedHeader, headerCells);
        }

        Assert.Contains("First Name", headerCells);
        Assert.Contains("Last Name", headerCells);
        Assert.Contains("Work Email", headerCells);
    }

    [Fact]
    public void Handle_EmployeeImportSheet_Has_Only_One_Header_Row_No_Data_Rows()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Employee Import");

        var usedRange = sheet.RangeUsed();
        Assert.NotNull(usedRange);
        Assert.Equal(1, usedRange!.LastRow().RowNumber());
    }

    [Fact]
    public void Handle_HeaderCells_Have_Comments()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Employee Import");

        var usedRange = sheet.RangeUsed();
        Assert.NotNull(usedRange);
        var lastColumn = usedRange!.LastColumn().ColumnNumber();

        for (var col = 1; col <= lastColumn; col++)
        {
            var cell = sheet.Cell(1, col);
            Assert.True(cell.HasComment, $"Expected header cell '{cell.GetString()}' to have a comment.");
            Assert.False(string.IsNullOrWhiteSpace(cell.GetComment().Text));
        }
    }

    [Fact]
    public void Handle_SalaryType_Column_Has_DataValidation_List_With_Expected_Values()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Employee Import");

        var usedRange = sheet.RangeUsed();
        Assert.NotNull(usedRange);
        var lastColumn = usedRange!.LastColumn().ColumnNumber();

        var salaryTypeColumn = -1;
        for (var col = 1; col <= lastColumn; col++)
        {
            if (sheet.Cell(1, col).GetString() == "Salary Type")
            {
                salaryTypeColumn = col;
                break;
            }
        }

        Assert.True(salaryTypeColumn > 0, "Expected a 'Salary Type' column in the header row.");

        var dataCell = sheet.Cell(2, salaryTypeColumn);
        var validation = sheet.DataValidations.FirstOrDefault(dv => dv.Ranges.Any(r => r.Contains(dataCell)));

        Assert.NotNull(validation);
        Assert.Equal(XLAllowedValues.List, validation!.AllowedValues);

        var listValue = validation.Value;
        Assert.Contains("Annual", listValue);
        Assert.Contains("Hourly", listValue);
        Assert.Contains("Daily", listValue);
    }

    // Regression test for a workbook-corruption bug: ClosedXML's validation.List() writes an
    // *unquoted* comma-separated string straight into formula1 unless the caller wraps it in
    // literal quotes itself. Excel requires an explicit-list formula1 to be a quoted string
    // literal ("A,B,C") — unquoted, it's parsed as an invalid range reference, and Excel drops
    // the data validation with a "Removed Feature: Data validation" repair prompt on open.
    // ClosedXML's own object model silently round-trips the bad value (it strips/re-adds quotes
    // when reading validation.Value back), so the higher-level test above wouldn't catch this —
    // only inspecting the raw sheet XML does.
    [Fact]
    public void Handle_SalaryType_DataValidation_Formula_Is_A_Quoted_List_Literal()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var package = Package.Open(new MemoryStream(bytes), FileMode.Open, FileAccess.Read);
        var sheetPart = package.GetParts()
            .Single(p => p.Uri.OriginalString.EndsWith("/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase));

        using var stream = sheetPart.GetStream();
        var doc = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var formula1 = doc.Descendants(ns + "dataValidation")
            .Single()
            .Element(ns + "formula1")!
            .Value;

        Assert.StartsWith("\"", formula1);
        Assert.EndsWith("\"", formula1);
    }

    [Fact]
    public void Handle_InstructionsSheet_Contains_Field_Required_Format_Table_For_Every_Header()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        var employeeImportSheet = workbook.Worksheet("Employee Import");
        var employeeUsedRange = employeeImportSheet.RangeUsed();
        Assert.NotNull(employeeUsedRange);
        var lastColumn = employeeUsedRange!.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var col = 1; col <= lastColumn; col++)
            headers.Add(employeeImportSheet.Cell(1, col).GetString());

        var instructions = workbook.Worksheet("Instructions");
        Assert.Equal("Field", instructions.Cell(1, 1).GetString());
        Assert.Equal("Required", instructions.Cell(1, 2).GetString());
        Assert.Equal("Format", instructions.Cell(1, 3).GetString());

        var usedRange = instructions.RangeUsed();
        Assert.NotNull(usedRange);
        var lastRow = usedRange!.LastRow().RowNumber();

        var instructionFields = new List<string>();
        for (var row = 2; row <= lastRow; row++)
        {
            var field = instructions.Cell(row, 1).GetString();
            instructionFields.Add(field);
            Assert.False(string.IsNullOrWhiteSpace(instructions.Cell(row, 2).GetString()));
            Assert.False(string.IsNullOrWhiteSpace(instructions.Cell(row, 3).GetString()));
        }

        foreach (var header in headers)
        {
            Assert.Contains(header, instructionFields);
        }
    }
}
