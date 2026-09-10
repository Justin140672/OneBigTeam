using System.IO.Compression;
using System.Text;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Story 2 / Ticket 4: assembles the organisation data export ZIP — one RFC 4180 CSV per
/// <see cref="DataExportTable"/> plus each supplied document at its ZIP path.
///
/// Ticket 4: the archive is written <b>straight to a caller-provided output stream</b> (a temp file),
/// never buffered in memory. Module tables are pulled from an <see cref="IAsyncEnumerable{T}"/> so the
/// caller can yield them one source at a time. Document streams are opened, copied and disposed
/// <b>one at a time</b> — never all held open. Lives in Abstractions so the Reporting module's build
/// job can use it without referencing HR.Infrastructure.
/// </summary>
public sealed class OrganisationDataExportPackageBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes the export archive into <paramref name="output"/>. Returns the list of expected document
    /// ZIP paths whose stream came back <c>null</c> (missing from storage); an empty list means every
    /// document was embedded. When documents are missing the archive content is not usable and the
    /// caller must discard <paramref name="output"/> and fail the export (preserving the pre-existing
    /// fail-whole-export behaviour).
    /// </summary>
    /// <param name="onEntryWritten">
    /// Invoked after every entry with the current archive length, so the caller can enforce a
    /// temp-disk ceiling. May throw to abort the build.
    /// </param>
    public async Task<IReadOnlyList<string>> BuildToStreamAsync(
        IAsyncEnumerable<DataExportTable> tables,
        IReadOnlyList<DocumentExportFileEntry> fileEntries,
        Func<DocumentExportFileEntry, CancellationToken, Task<Stream?>> openDocument,
        Stream output,
        Action<long>? onEntryWritten,
        CancellationToken cancellationToken)
    {
        // Ticket 3: guarantee every archive entry name is unique so a duplicate can never produce a
        // malformed archive that some ZIP readers reject.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await foreach (var table in tables.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Uniquify(usedNames, $"{SanitiseName(table.Name)}.csv");
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                await using (var writer = new StreamWriter(entryStream, Utf8NoBom))
                {
                    await WriteCsvAsync(writer, table, cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                onEntryWritten?.Invoke(output.Length);
            }

            foreach (var fileEntry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Ticket 4: exactly one document stream open at any moment.
                var content = await openDocument(fileEntry, cancellationToken).ConfigureAwait(false);
                if (content is null)
                {
                    missing.Add(NormaliseZipPath(fileEntry.ZipPath));
                    continue;
                }

                await using (content.ConfigureAwait(false))
                {
                    // Once a document is missing the archive is doomed; keep draining the remaining
                    // entries (one stream at a time) only to count every missing file for the alert.
                    if (missing.Count > 0)
                        continue;

                    var name = Uniquify(usedNames, NormaliseZipPath(fileEntry.ZipPath));
                    var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await content.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                }

                onEntryWritten?.Invoke(output.Length);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return missing;
    }

    /// <summary>
    /// Writes one RFC 4180 CSV. Streamed tables (<see cref="DataExportTable.RowStream"/>) are written
    /// row-by-row straight to <paramref name="writer"/> and never buffered into a list, so an
    /// effectively unbounded source stays memory-bounded. Byte output is identical to the eager path.
    /// </summary>
    private static async Task WriteCsvAsync(TextWriter writer, DataExportTable table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WriteLineAsync(writer, table.Columns.Select(EscapeField), cancellationToken).ConfigureAwait(false);

        if (table.RowStream is not null)
        {
            await foreach (var row in table.RowStream(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteLineAsync(writer, row.Select(EscapeField), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteLineAsync(writer, row.Select(EscapeField), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteLineAsync(
        TextWriter writer, IEnumerable<string> fields, CancellationToken cancellationToken)
    {
        await writer.WriteAsync((string.Join(",", fields) + "\r\n").AsMemory(), cancellationToken).ConfigureAwait(false);
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

    private static string Uniquify(HashSet<string> used, string path)
    {
        if (used.Add(path))
            return path;

        var slash = path.LastIndexOf('/');
        var dir = slash >= 0 ? path[..(slash + 1)] : string.Empty;
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;

        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : string.Empty;

        for (var i = 2; ; i++)
        {
            var candidate = $"{dir}{stem} ({i}){ext}";
            if (used.Add(candidate))
                return candidate;
        }
    }
}

/// <summary>
/// Ticket 4: forwards to an inner stream but ignores Dispose/DisposeAsync, so a storage client that
/// disposes the content it is handed (e.g. <see cref="System.Net.Http.StreamContent"/>) cannot close
/// the export's temp-file stream between upload retries.
/// </summary>
public sealed class NonDisposingStreamWrapper(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        // Intentionally does not dispose the inner stream.
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
