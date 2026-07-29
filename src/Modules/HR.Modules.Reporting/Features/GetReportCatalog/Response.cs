namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed record GetReportCatalogResponse(IReadOnlyList<ReportCatalogItem> Items);

internal sealed record ReportCatalogItem(
    string Id,
    string DisplayName,
    string Category,
    string Description);
