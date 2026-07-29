namespace HR.Modules.Reporting.Features.RenameReportView;

internal sealed record RenameReportViewRequest(Guid CompanyId, Guid ViewId, string Name);
