using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetMyProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetMyProfilePhotoHandlerTests
{
    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetMyProfilePhotoHandler BuildHandler(
        DocumentsDbContext db, FakeProfilePhotoStorageService? storage = null) =>
        new(db, storage ?? new FakeProfilePhotoStorageService());

    private static EmployeeProfilePhoto SeedCurrentPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId,
        FileScanStatus status = FileScanStatus.Clean, string storageKey = "current/key.png")
    {
        var photo = EmployeeProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "current.png", 111, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        ApplyScanStatus(photo, status);
        db.EmployeeProfilePhotos.Add(photo);
        db.SaveChanges();
        return photo;
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId,
        FileScanStatus status = FileScanStatus.Clean, string storageKey = "pending/key.png")
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "pending.png", 222, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        ApplyScanStatus(pending, status);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    private static void ApplyScanStatus(EmployeeProfilePhoto photo, FileScanStatus status)
    {
        switch (status)
        {
            case FileScanStatus.Pending: break; // Create() defaults to Pending
            case FileScanStatus.Clean: photo.MarkScanClean(DateTimeOffset.UtcNow); break;
            case FileScanStatus.Scanning: photo.MarkScanning(DateTimeOffset.UtcNow); break;
            case FileScanStatus.Infected: photo.MarkScanInfected("EICAR.Test.File", DateTimeOffset.UtcNow); break;
            case FileScanStatus.Failed: photo.MarkScanFailed("scanner unreachable", DateTimeOffset.UtcNow); break;
        }
    }

    private static void ApplyScanStatus(PendingProfilePhoto photo, FileScanStatus status)
    {
        switch (status)
        {
            case FileScanStatus.Pending: break; // Create() defaults to Pending
            case FileScanStatus.Clean: photo.MarkScanClean(DateTimeOffset.UtcNow); break;
            case FileScanStatus.Scanning: photo.MarkScanning(DateTimeOffset.UtcNow); break;
            case FileScanStatus.Infected: photo.MarkScanInfected("EICAR.Test.File", DateTimeOffset.UtcNow); break;
            case FileScanStatus.Failed: photo.MarkScanFailed("scanner unreachable", DateTimeOffset.UtcNow); break;
        }
    }

    [Fact]
    public async Task HandleAsync_Returns_Both_CurrentPhoto_And_PendingPhoto_When_Both_Exist()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var current          = SeedCurrentPhoto(db, companyId, employeeId);
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var handler           = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result.CurrentPhoto);
        Assert.NotNull(result.PendingPhoto);
        Assert.Equal(current.Id, result.CurrentPhoto!.Id);
        Assert.Equal(pending.Id, result.PendingPhoto!.Id);
        Assert.Contains(current.StorageKey, result.CurrentPhoto.DownloadUrl);
        Assert.Contains(pending.StorageKey, result.PendingPhoto.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_CurrentPhoto_Only_When_No_Pending_Submission_Exists()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var current          = SeedCurrentPhoto(db, companyId, employeeId);
        var handler           = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result.CurrentPhoto);
        Assert.Equal(current.Id, result.CurrentPhoto!.Id);
        Assert.Null(result.PendingPhoto);
    }

    [Fact]
    public async Task HandleAsync_Returns_PendingPhoto_Only_When_No_Live_Photo_Exists_Yet()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var handler            = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.CurrentPhoto);
        Assert.NotNull(result.PendingPhoto);
        Assert.Equal(pending.Id, result.PendingPhoto!.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Both_Null_When_Neither_Exists_And_Still_Succeeds()
    {
        await using var db = BuildContext();
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.CurrentPhoto);
        Assert.Null(result.PendingPhoto);
    }

    [Fact]
    public async Task HandleAsync_Scopes_By_CompanyId_And_EmployeeId()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        SeedCurrentPhoto(db, companyId, otherEmployeeId);
        SeedPendingPhoto(db, companyId, otherEmployeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.CurrentPhoto);
        Assert.Null(result.PendingPhoto);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Photos_From_Another_Company()
    {
        await using var db = BuildContext();
        var employeeId     = Guid.NewGuid();
        var companyId      = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        SeedCurrentPhoto(db, otherCompanyId, employeeId);
        SeedPendingPhoto(db, otherCompanyId, employeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.CurrentPhoto);
        Assert.Null(result.PendingPhoto);
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but FileScanStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)FileScanStatus.Pending)]
    [InlineData((int)FileScanStatus.Scanning)]
    [InlineData((int)FileScanStatus.Infected)]
    [InlineData((int)FileScanStatus.Failed)]
    public async Task HandleAsync_Omits_CurrentPhoto_When_Its_Scan_Is_Not_Clean(int statusValue)
    {
        var status = (FileScanStatus)statusValue;
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedCurrentPhoto(db, companyId, employeeId, status);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.CurrentPhoto);
    }

    [Theory]
    [InlineData((int)FileScanStatus.Pending)]
    [InlineData((int)FileScanStatus.Scanning)]
    [InlineData((int)FileScanStatus.Infected)]
    [InlineData((int)FileScanStatus.Failed)]
    public async Task HandleAsync_Omits_PendingPhoto_When_Its_Scan_Is_Not_Clean(int statusValue)
    {
        var status = (FileScanStatus)statusValue;
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId, status);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.PendingPhoto);
    }

    [Fact]
    public async Task HandleAsync_Includes_CurrentPhoto_But_Omits_PendingPhoto_When_Only_Pending_Scan_Fails()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var current    = SeedCurrentPhoto(db, companyId, employeeId, FileScanStatus.Clean);
        SeedPendingPhoto(db, companyId, employeeId, FileScanStatus.Infected);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result.CurrentPhoto);
        Assert.Equal(current.Id, result.CurrentPhoto!.Id);
        Assert.Null(result.PendingPhoto);
    }

    [Fact]
    public async Task HandleAsync_Includes_PendingPhoto_But_Omits_CurrentPhoto_When_Only_Current_Scan_Fails()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedCurrentPhoto(db, companyId, employeeId, FileScanStatus.Infected);
        var pending = SeedPendingPhoto(db, companyId, employeeId, FileScanStatus.Clean);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null(result.CurrentPhoto);
        Assert.NotNull(result.PendingPhoto);
        Assert.Equal(pending.Id, result.PendingPhoto!.Id);
    }
}
