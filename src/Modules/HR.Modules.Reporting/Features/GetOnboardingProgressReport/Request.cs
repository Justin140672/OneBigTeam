namespace HR.Modules.Reporting.Features.GetOnboardingProgressReport;

internal sealed record GetOnboardingProgressReportRequest(Guid CompanyId, bool OverdueOnly = false);
