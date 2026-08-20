using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DocumentTypeDefaultsProvisionerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureDefaultDocumentTypesAsync_Creates_Default_Set_When_None_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var provisioner = new DocumentTypeDefaultsProvisioner(context, new FakeClock(FixedUtcNow));

        await provisioner.EnsureDefaultDocumentTypesAsync(companyId, CancellationToken.None);

        var names = await context.DocumentTypes.Where(dt => dt.CompanyId == companyId).Select(dt => dt.Name).ToListAsync();

        Assert.Equal(
            new[] { "Contract", "Passport", "Driving Licence", "Right To Work", "Certificate", "Other" }.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public async Task EnsureDefaultDocumentTypesAsync_Does_Nothing_When_Company_Already_Has_DocumentTypes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.DocumentTypes.Add(DocumentType.Create(Guid.NewGuid(), companyId, "Custom Type", null, now));
        await context.SaveChangesAsync();

        var provisioner = new DocumentTypeDefaultsProvisioner(context, new FakeClock(FixedUtcNow));
        await provisioner.EnsureDefaultDocumentTypesAsync(companyId, CancellationToken.None);

        var documentType = await context.DocumentTypes.SingleAsync(dt => dt.CompanyId == companyId);
        Assert.Equal("Custom Type", documentType.Name);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
