using System.IO.Compression;
using System.Text;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportPackageBuilderTests
{
    private readonly OrganisationDataExportPackageBuilder _builder = new();

    private static ZipArchive Open(byte[] bytes) => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Build_Writes_One_Csv_Per_Table_With_Header_And_Crlf()
    {
        var table = new DataExportTable("employees",
            ["Id", "Name"],
            [new string?[] { "1", "Alice" }, new string?[] { "2", "Bob" }]);

        var bytes = _builder.Build([table], []);

        using var archive = Open(bytes);
        var csv = ReadEntry(archive, "employees.csv");
        Assert.Equal("Id,Name\r\n1,Alice\r\n2,Bob\r\n", csv);
    }

    [Fact]
    public void Build_Quotes_Fields_Containing_Comma_Quote_Or_Newline_And_Doubles_Quotes()
    {
        var table = new DataExportTable("t",
            ["A", "B", "C"],
            [new string?[] { "has,comma", "has\"quote", "line1\nline2" }]);

        var bytes = _builder.Build([table], []);

        using var archive = Open(bytes);
        var csv = ReadEntry(archive, "t.csv");
        Assert.Equal("A,B,C\r\n\"has,comma\",\"has\"\"quote\",\"line1\nline2\"\r\n", csv);
    }

    [Fact]
    public void Build_Renders_Null_Cells_As_Empty()
    {
        var table = new DataExportTable("t", ["A", "B"], [new string?[] { null, "x" }]);

        var bytes = _builder.Build([table], []);

        using var archive = Open(bytes);
        Assert.Equal("A,B\r\n,x\r\n", ReadEntry(archive, "t.csv"));
    }

    [Fact]
    public void Build_Adds_File_Entries_At_Their_Zip_Path()
    {
        var table = new DataExportTable("t", ["A"], []);
        using var content = new MemoryStream("PDF-BYTES"u8.ToArray());

        var bytes = _builder.Build([table], [("documents/Contracts/offer.pdf", content)]);

        using var archive = Open(bytes);
        Assert.NotNull(archive.GetEntry("t.csv"));
        Assert.Equal("PDF-BYTES", ReadEntry(archive, "documents/Contracts/offer.pdf"));
    }
}
