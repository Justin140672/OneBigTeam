namespace HR.Modules.Reporting.Features.GetDocumentComplianceReport;

internal sealed record GetDocumentComplianceReportRequest(Guid CompanyId, Guid? PositionProfileId);
