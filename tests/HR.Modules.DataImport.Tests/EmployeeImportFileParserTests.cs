using System.Text;
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

    private static Stream ToStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Parse_Csv_Maps_Headers_And_Rows_With_Correct_RowNumbers()
    {
        var csv =
            "First Name,Last Name,Work Email,Notes\n" +
            "Alice,Smith,alice@example.com,\"Likes coffee, tea\"\n" +
            "Bob,Jones,bob@example.com,No notes\n";

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(ToStream(csv), "employees.csv", Mapping);

        Assert.Equal(new HashSet<string> { "FirstName", "LastName", "WorkEmail", "Notes" }, result.MappedFields);
        Assert.Equal(2, result.Rows.Count);

        var row1 = result.Rows[0];
        Assert.Equal(2, row1.RowNumber); // header is row 1, first data row is row 2
        Assert.Equal("Alice", row1.Fields["FirstName"]);
        Assert.Equal("Smith", row1.Fields["LastName"]);
        Assert.Equal("alice@example.com", row1.Fields["WorkEmail"]);
        Assert.Equal("Likes coffee, tea", row1.Fields["Notes"]); // quoted field containing a comma

        var row2 = result.Rows[1];
        Assert.Equal(3, row2.RowNumber);
        Assert.Equal("Bob", row2.Fields["FirstName"]);
        Assert.Equal("Jones", row2.Fields["LastName"]);
        Assert.Equal("bob@example.com", row2.Fields["WorkEmail"]);
        Assert.Equal("No notes", row2.Fields["Notes"]);
    }

    [Fact]
    public void Parse_Csv_Column_Missing_From_Header_Is_Absent_From_MappedFields_And_Every_Row()
    {
        // "Work Email" header is not present anywhere in the file.
        var csv =
            "First Name,Last Name\n" +
            "Alice,Smith\n" +
            "Bob,Jones\n";

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(ToStream(csv), "employees.csv", Mapping);

        Assert.DoesNotContain("WorkEmail", result.MappedFields);
        Assert.DoesNotContain("Notes", result.MappedFields);
        Assert.All(result.Rows, r => Assert.False(r.Fields.ContainsKey("WorkEmail")));
        Assert.All(result.Rows, r => Assert.False(r.Fields.ContainsKey("Notes")));
    }

    [Fact]
    public void Parse_Csv_Empty_Or_Whitespace_Cells_Become_Null()
    {
        var csv =
            "First Name,Last Name,Work Email,Notes\n" +
            "Alice,Smith,alice@example.com,   \n" +
            "Bob,Jones,,\n";

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(ToStream(csv), "employees.csv", Mapping);

        Assert.Null(result.Rows[0].Fields["Notes"]);
        Assert.Null(result.Rows[1].Fields["WorkEmail"]);
        Assert.Null(result.Rows[1].Fields["Notes"]);
    }

    [Fact]
    public void Parse_Xlsx_Maps_Headers_And_Rows_With_Correct_RowNumbers()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "First Name";
        worksheet.Cell(1, 2).Value = "Last Name";
        worksheet.Cell(1, 3).Value = "Work Email";

        worksheet.Cell(2, 1).Value = "Alice";
        worksheet.Cell(2, 2).Value = "Smith";
        worksheet.Cell(2, 3).Value = "alice@example.com";

        worksheet.Cell(3, 1).Value = "Bob";
        worksheet.Cell(3, 2).Value = "Jones";
        worksheet.Cell(3, 3).Value = "bob@example.com";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, "employees.xlsx", Mapping);

        Assert.Equal(new HashSet<string> { "FirstName", "LastName", "WorkEmail" }, result.MappedFields);
        Assert.Equal(2, result.Rows.Count);

        var row1 = result.Rows[0];
        Assert.Equal(2, row1.RowNumber);
        Assert.Equal("Alice", row1.Fields["FirstName"]);
        Assert.Equal("Smith", row1.Fields["LastName"]);
        Assert.Equal("alice@example.com", row1.Fields["WorkEmail"]);

        var row2 = result.Rows[1];
        Assert.Equal(3, row2.RowNumber);
        Assert.Equal("Bob", row2.Fields["FirstName"]);
        Assert.Equal("Jones", row2.Fields["LastName"]);
        Assert.Equal("bob@example.com", row2.Fields["WorkEmail"]);
    }

    [Fact]
    public void Parse_Xlsx_Column_Missing_From_Header_Is_Absent_From_MappedFields_And_Every_Row()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "First Name";
        worksheet.Cell(1, 2).Value = "Last Name";

        worksheet.Cell(2, 1).Value = "Alice";
        worksheet.Cell(2, 2).Value = "Smith";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var parser = new EmployeeImportFileParser();
        var result = parser.Parse(stream, "employees.xlsx", Mapping);

        Assert.DoesNotContain("WorkEmail", result.MappedFields);
        Assert.All(result.Rows, r => Assert.False(r.Fields.ContainsKey("WorkEmail")));
    }
}
