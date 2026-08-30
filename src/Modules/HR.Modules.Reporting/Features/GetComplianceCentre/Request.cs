namespace HR.Modules.Reporting.Features.GetComplianceCentre;

internal sealed record GetComplianceCentreRequest(
    Guid CompanyId,
    string? Category = null,
    string? Department = null,
    Guid? ManagerId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    string? Severity = null);
