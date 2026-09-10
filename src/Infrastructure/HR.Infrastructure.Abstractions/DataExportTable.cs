namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Generic, column-oriented tabular payload contributed by a module to a full organisation
/// data export (account-closure export). Each source module returns one or more of these; the
/// export job serialises every table to an RFC 4180 CSV inside the export ZIP.
/// Rows must never contain sensitive values that the owning module would not already surface
/// to a company administrator; audit rows are redacted by the existing scrubber.
///
/// Ticket 4 (memory hardening): a table may supply its rows either eagerly (<see cref="Rows"/>) or
/// as a bounded, keyset-paged async stream (<see cref="RowStream"/>). The package builder writes
/// streamed rows straight to the CSV entry one at a time and never buffers them into a list, so an
/// effectively unbounded source (the audit log) stays memory-bounded. Sources whose row count is
/// bounded by organisation size keep using the eager shape.
/// </summary>
public sealed record DataExportTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows)
{
    /// <summary>
    /// Optional streamed row source. When non-null the package builder enumerates this instead of
    /// <see cref="Rows"/>, writing each row directly to the CSV stream and discarding it. The factory
    /// receives the build cancellation token and must honour it while fetching and while enumerating.
    /// </summary>
    public Func<CancellationToken, IAsyncEnumerable<IReadOnlyList<string?>>>? RowStream { get; init; }

    /// <summary>
    /// Creates a table whose rows are produced by a bounded async stream (e.g. keyset-paged database
    /// reads) rather than materialised up front.
    /// </summary>
    public static DataExportTable Streamed(
        string name,
        IReadOnlyList<string> columns,
        Func<CancellationToken, IAsyncEnumerable<IReadOnlyList<string?>>> rowStream) =>
        new(name, columns, []) { RowStream = rowStream };
}
