using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AddRequiredDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Adds_RequiredDocument_To_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var documentTypeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, auditPublisher, documentTypeExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                DocumentTypeId = documentTypeId,
                IsMandatory = true,
                DueDaysAfterStart = 7,
                RequiresExpiryDate = false
            },
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profile.Id, result.Value!.PositionProfileId);
        Assert.Equal(documentTypeId, result.Value.DocumentTypeId);
        Assert.True(result.Value.IsMandatory);
        Assert.Equal(7, result.Value.DueDaysAfterStart);
        Assert.False(result.Value.RequiresExpiryDate);

        var saved = await context.PositionProfileRequiredDocuments.SingleAsync();
        Assert.Equal(profile.Id, saved.PositionProfileId);
        Assert.True(saved.IsActive);

        Assert.Single(auditPublisher.Published);
        var auditEvent = auditPublisher.Published[0];
        Assert.Equal("position-profile.required-document.added", auditEvent.EventType);
        Assert.Equal(profile.Id, auditEvent.EntityId);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher(), documentTypeExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                DocumentTypeId = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Engineer", null, false, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), documentTypeExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
                DocumentTypeId = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), documentTypeExists: false);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                DocumentTypeId = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_DocumentType_Already_Required()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var documentTypeId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var existing = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, documentTypeId, true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), documentTypeExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                DocumentTypeId = documentTypeId,
                IsMandatory = false
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_DocumentType_On_Different_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var documentTypeId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Manager", null, true, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var existing = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profileA.Id, documentTypeId, true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), documentTypeExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profileB.Id,
                DocumentTypeId = documentTypeId
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static AddRequiredDocumentHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher auditPublisher,
        bool documentTypeExists)
    {
        return new AddRequiredDocumentHandler(
            context,
            new StubDocumentTypeReader(documentTypeExists),
            new FakeClock(FixedUtcNow),
            auditPublisher);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private sealed class StubDocumentTypeReader(bool exists) : IDocumentTypeReader
    {
        public Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken)
            => Task.FromResult(exists);

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> documentTypeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
