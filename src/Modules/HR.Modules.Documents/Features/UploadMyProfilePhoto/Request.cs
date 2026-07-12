using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class UploadMyProfilePhotoRequest
{
    public Guid CompanyId { get; init; }
    public IFormFile File { get; init; } = null!;
}
