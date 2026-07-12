namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed record RejectProfilePhotoRequest(Guid CompanyId, Guid EmployeeId, string? RejectionReason);
