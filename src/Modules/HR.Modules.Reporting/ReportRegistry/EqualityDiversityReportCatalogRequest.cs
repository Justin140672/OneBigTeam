namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Catalogue shim for the anonymous Equality &amp; Diversity report. The report itself is owned by
/// HR.Modules.Employees (it needs the encrypted equality data), and its real request type is
/// internal to that module, so the registry describes it with this local stand-in. The report
/// takes no filter/grouping/sorting parameters — it always returns the full anonymous aggregate —
/// so this carries only <see cref="CompanyId"/>, which <see cref="ReportCatalog"/> excludes from
/// the exposed field list like every other entry.
/// </summary>
internal sealed record EqualityDiversityReportCatalogRequest(Guid CompanyId);
