using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetPendingProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetPendingProfilePhotoHandlerTests
{
    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetPendingProfilePhotoHandler BuildHandler(
        DocumentsDbContext db, FakeProfilePhotoStorageService? storage = null) =>
        new(db, storage ?? new FakeProfilePhotoStorageService());

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "pending/key.png")
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "pending.png", 222, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    [Fact]
    public async Task HandleAsync_Returns_PendingPhoto_With_DownloadUrl_When_One_Exists()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var handler            = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(pending.Id,         result.Value!.Id);
        Assert.Equal(companyId,          result.Value.CompanyId);
        Assert.Equal(employeeId,         result.Value.EmployeeId);
        Assert.Equal(pending.FileName,   result.Value.FileName);
        Assert.Equal(pending.FileSize,   result.Value.FileSize);
        Assert.Equal(pending.ContentType, result.Value.ContentType);
        Assert.Contains(pending.StorageKey, result.Value.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_None_Exists()
    {
        await using var db = BuildContext();
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Another_Employees_Pending_Photo()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        SeedPendingPhoto(db, companyId, otherEmployeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Pending_Photo_From_Another_Company()
    {
        await using var db = BuildContext();
        var employeeId       = Guid.NewGuid();
        var companyId        = Guid.NewGuid();
        var otherCompanyId   = Guid.NewGuid();

        SeedPendingPhoto(db, otherCompanyId, employeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
