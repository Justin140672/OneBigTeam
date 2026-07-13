using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetPendingProfilePhotoById;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetPendingProfilePhotoByIdHandlerTests
{
    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetPendingProfilePhotoByIdHandler BuildHandler(
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
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId);
        var handler    = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoByIdRequest(companyId, pending.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(pending.Id,          result.Value!.Id);
        Assert.Equal(companyId,           result.Value.CompanyId);
        Assert.Equal(employeeId,          result.Value.EmployeeId);
        Assert.Equal(pending.FileName,    result.Value.FileName);
        Assert.Equal(pending.FileSize,    result.Value.FileSize);
        Assert.Equal(pending.ContentType, result.Value.ContentType);
        Assert.Contains(pending.StorageKey, result.Value.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_None_Exists()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoByIdRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Pending_Photo_From_Another_Company()
    {
        // Same pending-photo Id, but the request's CompanyId doesn't match the row's CompanyId —
        // this is the tenant-isolation guarantee for the by-id lookup (Id alone is not enough).
        await using var db = BuildContext();
        var employeeId     = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        var pending = SeedPendingPhoto(db, otherCompanyId, employeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoByIdRequest(callerCompanyId, pending.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist_Even_If_Company_Has_Other_Pending_Photos()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        SeedPendingPhoto(db, companyId, Guid.NewGuid());

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetPendingProfilePhotoByIdRequest(companyId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
