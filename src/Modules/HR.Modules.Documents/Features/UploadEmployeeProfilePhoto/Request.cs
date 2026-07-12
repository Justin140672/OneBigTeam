using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;

internal sealed class UploadEmployeeProfilePhotoRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public IFormFile File { get; init; } = null!;
}
