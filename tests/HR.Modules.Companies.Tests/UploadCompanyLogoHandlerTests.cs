using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UploadCompanyLogo;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Storage;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UploadCompanyLogoHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UploadCompanyLogoHandler(
            context,
            new StubBrandingStorage(),
            new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new UploadCompanyLogoRequest
            {
                CompanyId = Guid.NewGuid(),
                AssetType = BrandingAssetType.PrimaryLogo,
                FileName = "logo.png",
                ContentType = "image/png",
                FileSizeBytes = 1024,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Branding_Record_When_None_Exists()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UploadCompanyLogoHandler(
            context,
            new StubBrandingStorage(),
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new UploadCompanyLogoRequest
            {
                CompanyId = company.Id,
                AssetType = BrandingAssetType.PrimaryLogo,
                FileName = "logo.png",
                ContentType = "image/png",
                FileSizeBytes = 2048,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.Equal(BrandingAssetType.PrimaryLogo, result.Value.AssetType);
        Assert.Contains("primary-logo", result.Value.LogoUrl);
        Assert.Contains("logo.png", result.Value.LogoUrl);

        var branding = await context.CompanyBranding.SingleAsync();
        Assert.Equal(company.Id, branding.CompanyId);
        Assert.NotNull(branding.PrimaryLogoUrl);
        Assert.Null(branding.SmallLogoUrl);
        Assert.Null(branding.EmailLogoUrl);
    }

    [Fact]
    public async Task HandleAsync_Updates_Existing_Branding_Record()
    {
        await using var context = BuildContext();
        var createdAt = new DateTimeOffset(new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", createdAt);
        context.Companies.Add(company);

        var branding = CompanyBranding.CreateDefault(company.Id, createdAt);
        branding.SetLogoUrl(BrandingAssetType.PrimaryLogo, "/old/primary.png", createdAt);
        context.CompanyBranding.Add(branding);
        await context.SaveChangesAsync();

        var handler = new UploadCompanyLogoHandler(
            context,
            new StubBrandingStorage(),
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new UploadCompanyLogoRequest
            {
                CompanyId = company.Id,
                AssetType = BrandingAssetType.SmallLogo,
                FileName = "small.svg",
                ContentType = "image/svg+xml",
                FileSizeBytes = 512,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BrandingAssetType.SmallLogo, result.Value!.AssetType);

        var savedBranding = await context.CompanyBranding.SingleAsync();
        Assert.NotNull(savedBranding.PrimaryLogoUrl);
        Assert.NotNull(savedBranding.SmallLogoUrl);
        Assert.Contains("small-logo", savedBranding.SmallLogoUrl);
        Assert.Contains("small.svg", savedBranding.SmallLogoUrl);
    }

    [Fact]
    public async Task HandleAsync_Sets_Correct_Url_For_Each_Asset_Type()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UploadCompanyLogoHandler(
            context,
            new StubBrandingStorage(),
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var emailResult = await handler.HandleAsync(
            new UploadCompanyLogoRequest
            {
                CompanyId = company.Id,
                AssetType = BrandingAssetType.EmailLogo,
                FileName = "email.png",
                ContentType = "image/png",
                FileSizeBytes = 1024,
            },
            CancellationToken.None);

        Assert.True(emailResult.IsSuccess);
        Assert.Contains("email-logo", emailResult.Value!.LogoUrl);

        var branding = await context.CompanyBranding.SingleAsync();
        Assert.NotNull(branding.EmailLogoUrl);
        Assert.Null(branding.PrimaryLogoUrl);
        Assert.Null(branding.SmallLogoUrl);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
