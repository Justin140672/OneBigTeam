using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class ProfilePhotoReader(
    DocumentsDbContext dbContext,
    IProfilePhotoStorageService storage) : IProfilePhotoReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetCurrentPhotoUrlsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var photos = await dbContext.EmployeeProfilePhotos
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && ids.Contains(p.EmployeeId))
            .Select(p => new { p.EmployeeId, p.StorageKey })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, string>();

        foreach (var photo in photos)
        {
            var downloadUrl = await storage.GetDownloadUrlAsync(photo.StorageKey, cancellationToken);
            result[photo.EmployeeId] = downloadUrl.ToString();
        }

        return result;
    }
}
