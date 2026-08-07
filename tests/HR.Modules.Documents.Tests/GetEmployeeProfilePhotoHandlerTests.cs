using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetEmployeeProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeProfilePhotoHandlerTests
{
    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetEmployeeProfilePhotoHandler BuildHandler(
        DocumentsDbContext db, FakeProfilePhotoStorageService? storage = null) =>
        new(db, storage ?? new FakeProfilePhotoStorageService());

    private static EmployeeProfilePhoto SeedPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "employees/key.png")
    {
        var photo = EmployeeProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "avatar.png", 333, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        // Default download-success fixtures assume a clean scan.
        photo.MarkScanClean(DateTimeOffset.UtcNow);
        db.EmployeeProfilePhotos.Add(photo);
        db.SaveChanges();
        return photo;
    }

    [Fact]
    public async Task HandleAsync_Returns_Photo_With_DownloadUrl_When_One_Exists()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var photo      = SeedPhoto(db, companyId, employeeId);
        var handler    = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(photo.Id,          result.Value!.Id);
        Assert.Equal(companyId,         result.Value.CompanyId);
        Assert.Equal(employeeId,        result.Value.EmployeeId);
        Assert.Equal(photo.FileName,    result.Value.FileName);
        Assert.Equal(photo.FileSize,    result.Value.FileSize);
        Assert.Equal(photo.ContentType, result.Value.ContentType);
        Assert.Contains(photo.StorageKey, result.Value.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_None_Exists()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Another_Employees_Photo()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        SeedPhoto(db, companyId, otherEmployeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Photo_From_Another_Company()
    {
        await using var db = BuildContext();
        var employeeId     = Guid.NewGuid();
        var companyId      = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        SeedPhoto(db, otherCompanyId, employeeId);

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but FileScanStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)FileScanStatus.Pending, "This document is currently being security checked.")]
    [InlineData((int)FileScanStatus.Scanning, "This document is currently being security checked.")]
    [InlineData((int)FileScanStatus.Infected, "This document failed a security scan.")]
    [InlineData((int)FileScanStatus.Failed, "This document failed a security scan.")]
    public async Task HandleAsync_Returns_Validation_When_Photo_Is_Not_Clean(
        int statusValue, string expectedMessage)
    {
        var status = (FileScanStatus)statusValue;
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var photo = EmployeeProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "avatar.png", 333, "image/png",
            "employees/key.png", employeeId, DateTimeOffset.UtcNow);
        switch (status)
        {
            case FileScanStatus.Pending: break; // Create() defaults to Pending
            case FileScanStatus.Scanning: photo.MarkScanning(DateTimeOffset.UtcNow); break;
            case FileScanStatus.Infected: photo.MarkScanInfected("EICAR.Test.File", DateTimeOffset.UtcNow); break;
            case FileScanStatus.Failed: photo.MarkScanFailed("scanner unreachable", DateTimeOffset.UtcNow); break;
        }
        db.EmployeeProfilePhotos.Add(photo);
        db.SaveChanges();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetEmployeeProfilePhotoRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(expectedMessage, result.Error.Message);
    }
}
