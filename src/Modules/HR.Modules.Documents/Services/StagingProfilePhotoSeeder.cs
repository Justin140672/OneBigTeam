using System.Reflection;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Documents.Services;

internal static class StagingProfilePhotoSeeder
{
    private const string ResourceMarker = ".Seed.StagingPhotos.employee-";

    public static async Task SeedAsync(
        IServiceProvider services,
        Guid companyId,
        IReadOnlyDictionary<int, Guid> employeeIdsByNumber)
    {
        var db = services.GetRequiredService<DocumentsDbContext>();
        var storage = services.GetRequiredService<IProfilePhotoStorageService>();
        var assembly = typeof(StagingProfilePhotoSeeder).Assembly;
        var now = DateTimeOffset.UtcNow;

        foreach (var (number, employeeId) in employeeIdsByNumber)
        {
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"{ResourceMarker}{number}.jpg", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
                continue;

            if (await db.EmployeeProfilePhotos.AnyAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId))
                continue;

            await using var content = assembly.GetManifestResourceStream(resourceName)!;
            var fileName = $"employee-{number}.jpg";
            var storageKey = await storage.UploadAsync(
                content, fileName, "image/jpeg", $"{companyId}/{employeeId}", CancellationToken.None);

            var photo = EmployeeProfilePhoto.Create(
                Guid.NewGuid(), companyId, employeeId, fileName, content.Length, "image/jpeg", storageKey, employeeId, now);
            photo.MarkScanClean(now);
            db.EmployeeProfilePhotos.Add(photo);
        }

        await db.SaveChangesAsync();
    }
}
