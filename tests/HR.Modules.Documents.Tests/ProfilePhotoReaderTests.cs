using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ProfilePhotoReaderTests
{
    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ProfilePhotoReader BuildReader(
        DocumentsDbContext db, FakeProfilePhotoStorageService? storage = null) =>
        new(db, storage ?? new FakeProfilePhotoStorageService());

    private static EmployeeProfilePhoto SeedLivePhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey)
    {
        var photo = EmployeeProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "avatar.png", 111, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.EmployeeProfilePhotos.Add(photo);
        db.SaveChanges();
        return photo;
    }

    [Fact]
    public async Task GetCurrentPhotoUrlsAsync_Returns_Url_For_Employee_With_Live_Photo()
    {
        await using var db = BuildContext();
        var storage     = new FakeProfilePhotoStorageService();
        var companyId   = Guid.NewGuid();
        var employeeId  = Guid.NewGuid();
        var photo       = SeedLivePhoto(db, companyId, employeeId, "live/storage/key.png");
        var reader      = BuildReader(db, storage);

        var result = await reader.GetCurrentPhotoUrlsAsync(companyId, [employeeId], CancellationToken.None);

        Assert.True(result.ContainsKey(employeeId));
        Assert.Contains(photo.StorageKey, result[employeeId]);
    }

    [Fact]
    public async Task GetCurrentPhotoUrlsAsync_Excludes_Employees_From_Other_Companies()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var otherCompanyId  = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();

        // Same employeeId, but the live photo belongs to a different company.
        SeedLivePhoto(db, otherCompanyId, employeeId, "other-company/storage/key.png");

        var reader = BuildReader(db);

        var result = await reader.GetCurrentPhotoUrlsAsync(companyId, [employeeId], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCurrentPhotoUrlsAsync_Employee_Without_Live_Photo_Is_Absent_Not_Errored()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var withPhotoId    = Guid.NewGuid();
        var withoutPhotoId = Guid.NewGuid();

        SeedLivePhoto(db, companyId, withPhotoId, "live/storage/key.png");

        var reader = BuildReader(db);

        var result = await reader.GetCurrentPhotoUrlsAsync(
            companyId, [withPhotoId, withoutPhotoId], CancellationToken.None);

        Assert.True(result.ContainsKey(withPhotoId));
        Assert.False(result.ContainsKey(withoutPhotoId));
    }

    [Fact]
    public async Task GetCurrentPhotoUrlsAsync_Returns_Empty_Dictionary_When_No_EmployeeIds_Supplied()
    {
        await using var db = BuildContext();
        var reader = BuildReader(db);

        var result = await reader.GetCurrentPhotoUrlsAsync(
            Guid.NewGuid(), [], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCurrentPhotoUrlsAsync_Returns_Urls_For_Multiple_Employees_In_Same_Company()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employee1  = Guid.NewGuid();
        var employee2  = Guid.NewGuid();

        var photo1 = SeedLivePhoto(db, companyId, employee1, "employee1/key.png");
        var photo2 = SeedLivePhoto(db, companyId, employee2, "employee2/key.png");

        var reader = BuildReader(db);

        var result = await reader.GetCurrentPhotoUrlsAsync(
            companyId, [employee1, employee2], CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(photo1.StorageKey, result[employee1]);
        Assert.Contains(photo2.StorageKey, result[employee2]);
    }
}
