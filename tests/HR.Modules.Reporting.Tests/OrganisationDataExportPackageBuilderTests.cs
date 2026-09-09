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

    // ----- Ticket 3: archive entry names are de-duplicated -----

    [Fact]
    public void Build_Disambiguates_Two_File_Entries_With_The_Same_Zip_Path()
    {
        var table = new DataExportTable("t", ["A"], []);
        using var first = new MemoryStream("FIRST"u8.ToArray());
        using var second = new MemoryStream("SECOND"u8.ToArray());

        var bytes = _builder.Build([table],
        [
            ("documents/Contracts/offer.pdf", first),
            ("documents/Contracts/offer.pdf", second),
        ]);

        using var archive = Open(bytes);
        Assert.Equal(2, archive.Entries.Count(e => e.FullName.StartsWith("documents/Contracts/offer")));
        Assert.Equal("FIRST", ReadEntry(archive, "documents/Contracts/offer.pdf"));
        Assert.Equal("SECOND", ReadEntry(archive, "documents/Contracts/offer (2).pdf"));
    }

    [Fact]
    public void Build_Disambiguates_A_File_Entry_That_Collides_With_A_Table_Csv_Name()
    {
        var table = new DataExportTable("report", ["A"], [new string?[] { "row" }]);
        using var content = new MemoryStream("FILE-BODY"u8.ToArray());

        var bytes = _builder.Build([table], [("report.csv", content)]);

        using var archive = Open(bytes);
        Assert.Equal("A\r\nrow\r\n", ReadEntry(archive, "report.csv"));
        Assert.Equal("FILE-BODY", ReadEntry(archive, "report (2).csv"));
    }

    [Fact]
    public void Build_Disambiguates_Three_Identical_Names_As_2_And_3()
    {
        using var a = new MemoryStream("A"u8.ToArray());
        using var b = new MemoryStream("B"u8.ToArray());
        using var c = new MemoryStream("C"u8.ToArray());

        var bytes = _builder.Build([],
        [
            ("documents/x/file.pdf", a),
            ("documents/x/file.pdf", b),
            ("documents/x/file.pdf", c),
        ]);

        using var archive = Open(bytes);
        Assert.Equal("A", ReadEntry(archive, "documents/x/file.pdf"));
        Assert.Equal("B", ReadEntry(archive, "documents/x/file (2).pdf"));
        Assert.Equal("C", ReadEntry(archive, "documents/x/file (3).pdf"));
    }
}
