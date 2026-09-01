using System.IO.Compression;
using System.Text;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Story 2: pure, dependency-free builder that assembles the organisation data export ZIP —
/// one RFC 4180 CSV per <see cref="DataExportTable"/> plus each supplied file entry at its ZIP
/// path. No I/O beyond the in-memory archive; unit-tested directly. Lives in Abstractions so the
/// Reporting module's build job can use it without referencing HR.Infrastructure.
/// </summary>
public sealed class OrganisationDataExportPackageBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public byte[] Build(
        IReadOnlyList<DataExportTable> tables,
        IReadOnlyList<(string ZipPath, Stream Content)> files)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in tables)
            {
                var entry = archive.CreateEntry($"{SanitiseName(table.Name)}.csv", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Utf8NoBom);
                WriteCsv(writer, table);
            }

            foreach (var (zipPath, content) in files)
            {
                var entry = archive.CreateEntry(NormaliseZipPath(zipPath), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                content.CopyTo(entryStream);
            }
        }

        return buffer.ToArray();
    }

    private static void WriteCsv(TextWriter writer, DataExportTable table)
    {
        writer.Write(string.Join(",", table.Columns.Select(EscapeField)));
        writer.Write("\r\n");

        foreach (var row in table.Rows)
        {
            writer.Write(string.Join(",", row.Select(EscapeField)));
            writer.Write("\r\n");
        }
    }

    private static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.IndexOfAny(['"', ',', '\r', '\n']) >= 0;
        return needsQuoting ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static string SanitiseName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.Length == 0 ? "table" : sb.ToString();
    }

    private static string NormaliseZipPath(string zipPath) =>
        zipPath.Replace('\\', '/').TrimStart('/');
}
