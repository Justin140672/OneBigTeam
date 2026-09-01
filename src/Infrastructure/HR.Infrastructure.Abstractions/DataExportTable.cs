namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Generic, column-oriented tabular payload contributed by a module to a full organisation
/// data export (account-closure export). Each source module returns one or more of these; the
/// export job serialises every table to an RFC 4180 CSV inside the export ZIP.
/// Rows must never contain sensitive values that the owning module would not already surface
/// to a company administrator; audit rows are redacted by the existing scrubber.
/// </summary>
public sealed record DataExportTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows);
