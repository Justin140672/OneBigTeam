using ClosedXML.Excel;
using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests;

public class EmployeeImportFileParserTests
{
    private static readonly ColumnMappingProfile Mapping = new(new Dictionary<string, string>
    {
        ["FirstName"] = "First Name",
        ["LastName"] = "Last Name",
        ["WorkEmail"] = "Work Email",
        ["Notes"] = "Notes",
    });

    private static Stream ToXlsxStream(string[] headers, IEnumerable<string?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        for (var col = 0; col < headers.Length; col++)
            worksheet.Cell(1, col + 1).Value = headers[col];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
                worksheet.Cell(rowIndex, col + 1).Value = row[col];
            rowIndex++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static Stream EmptyXlsxStream()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Sheet1");

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Parse_Xlsx_Formats_Native_Date_Cells_As_Yyyy_MM_Dd_Regardless_Of_Excel_Display_Format()
    {
        // Regression test: a genuine Excel date cell (as opposed to a plain text cell) previously
        // came back from ClosedXML's GetString() with a trailing time component (e.g.
        // "01/08/2026 00:00:00") that neither of EmployeeStagingRowValidator's expected date
        // formats (yyyy-MM-dd / dd/MM/yyyy) could parse, incorrectly rejecting a legitimately
        // entered date. The parser must normalize any date/time-typed cell to yyyy-MM-dd itself,
        // rather than trusting the cell's raw display string.
        var mapping = new ColumnMappingProfile(new Dictionary<string, string> { ["StartDate"] = "Start Date" });

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");
        worksheet.Cell(1, 1).Value = "Start Date";
        worksheet.Cell(2, 1).Value = new DateTime(2026, 8, 1); // a real Excel date cell, not text
        worksheet.Cell(2, 1).Style.DateFormat.Format = "dd/mm/yyyy hh:mm:ss";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, mapping);

        var row = Assert.Single(result.Rows);
        Assert.Equal("2026-08-01", row.Fields["StartDate"]);
    }

    [Fact]
    public void Parse_Xlsx_Maps_Headers_And_Rows_With_Correct_RowNumbers_And_Values()
    {
        var stream = ToXlsxStream(
            ["First Name", "Last Name", "Work Email", "Notes"],
            [
                ["Alice", "Smith", "alice@example.com", "Likes coffee, tea"],
                ["Bob", "Jones", "bob@example.com", "No notes"],
            ]);

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, Mapping);

        Assert.Equal(new HashSet<string> { "FirstName", "LastName", "WorkEmail", "Notes" }, result.MappedFields);
        Assert.Equal(2, result.Rows.Count);

        var row1 = result.Rows[0];
        Assert.Equal(2, row1.RowNumber); // header is row 1, first data row is row 2
        Assert.Equal("Alice", row1.Fields["FirstName"]);
        Assert.Equal("Smith", row1.Fields["LastName"]);
        Assert.Equal("alice@example.com", row1.Fields["WorkEmail"]);
        Assert.Equal("Likes coffee, tea", row1.Fields["Notes"]);

        var row2 = result.Rows[1];
        Assert.Equal(3, row2.RowNumber);
        Assert.Equal("Bob", row2.Fields["FirstName"]);
        Assert.Equal("Jones", row2.Fields["LastName"]);
        Assert.Equal("bob@example.com", row2.Fields["WorkEmail"]);
        Assert.Equal("No notes", row2.Fields["Notes"]);
    }

    [Fact]
    public void Parse_Xlsx_Column_Missing_From_Header_Is_Absent_From_MappedFields_And_Every_Row()
    {
        // "Work Email" and "Notes" headers are not present anywhere in the file.
        var stream = ToXlsxStream(
            ["First Name", "Last Name"],
            [
                ["Alice", "Smith"],
                ["Bob", "Jones"],
            ]);

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, Mapping);

        Assert.DoesNotContain("WorkEmail", result.MappedFields);
        Assert.DoesNotContain("Notes", result.MappedFields);
        Assert.All(result.Rows, r => Assert.False(r.Fields.ContainsKey("WorkEmail")));
        Assert.All(result.Rows, r => Assert.False(r.Fields.ContainsKey("Notes")));
    }

    [Fact]
    public void Parse_Xlsx_Empty_Or_Whitespace_Cells_Become_Null()
    {
        var stream = ToXlsxStream(
            ["First Name", "Last Name", "Work Email", "Notes"],
            [
                ["Alice", "Smith", "alice@example.com", "   "],
                ["Bob", "Jones", "", ""],
            ]);

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, Mapping);

        Assert.Null(result.Rows[0].Fields["Notes"]);
        Assert.Null(result.Rows[1].Fields["WorkEmail"]);
        Assert.Null(result.Rows[1].Fields["Notes"]);
    }

    [Fact]
    public void Parse_Xlsx_Blank_Rows_Are_Skipped()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "First Name";
        worksheet.Cell(1, 2).Value = "Last Name";
        worksheet.Cell(1, 3).Value = "Work Email";

        worksheet.Cell(2, 1).Value = "Alice";
        worksheet.Cell(2, 2).Value = "Smith";
        worksheet.Cell(2, 3).Value = "alice@example.com";

        // Row 3 is entirely blank.

        worksheet.Cell(4, 1).Value = "Bob";
        worksheet.Cell(4, 2).Value = "Jones";
        worksheet.Cell(4, 3).Value = "bob@example.com";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, Mapping);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(2, result.Rows[0].RowNumber);
        Assert.Equal(4, result.Rows[1].RowNumber);
    }

    [Fact]
    public void Parse_Empty_Workbook_Returns_No_MappedFields_And_No_Rows()
    {
        var stream = EmptyXlsxStream();

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, Mapping);

        Assert.Empty(result.MappedFields);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ParseHeaders_Xlsx_Returns_Header_Row()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "First Name";
        worksheet.Cell(1, 2).Value = "Last Name";
        worksheet.Cell(1, 3).Value = "Work Email";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var parser = new EmployeeImportFileParser();
        var headers = parser.ParseHeaders(stream);

        Assert.Equal(new[] { "First Name", "Last Name", "Work Email" }, headers);
    }

    [Fact]
    public void ParseHeaders_Empty_Workbook_Returns_Empty_List()
    {
        var stream = EmptyXlsxStream();

        var parser = new EmployeeImportFileParser();
        var headers = parser.ParseHeaders(stream);

        Assert.Empty(headers);
    }
}
