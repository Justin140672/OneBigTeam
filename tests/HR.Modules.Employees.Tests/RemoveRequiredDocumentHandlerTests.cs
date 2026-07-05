using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class RemoveRequiredDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Active_RequiredDocument()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var doc = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, 30, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(doc);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, auditPublisher);
        var result = await handler.HandleAsync(
            new RemoveRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                Id = doc.Id
            },
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.PositionProfileRequiredDocuments.SingleAsync();
        Assert.False(saved.IsActive);

        Assert.Single(auditPublisher.Published);
        var evt = auditPublisher.Published[0];
        Assert.Equal("position-profile.required-document.removed", evt.EventType);
        Assert.Equal(profile.Id, evt.EntityId);
        Assert.Equal(actorId, evt.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new RemoveRequiredDocumentRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                Id = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var doc = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        doc.Deactivate();
        context.PositionProfileRequiredDocuments.Add(doc);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                Id = doc.Id
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var doc = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(doc);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveRequiredDocumentRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
                Id = doc.Id
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Engineer", null, false, null, null, null, null, null, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Manager", null, true, null, null, null, null, null, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var doc = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profileA.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(doc);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveRequiredDocumentRequest
            {
                CompanyId = companyId,
                PositionProfileId = profileB.Id,
                Id = doc.Id
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static RemoveRequiredDocumentHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher auditPublisher)
        => new(context, new FakeClock(FixedUtcNow), auditPublisher);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
