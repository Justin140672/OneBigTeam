using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AddRequiredAssetHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Adds_RequiredAsset_To_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assetCategoryId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, auditPublisher, assetCategoryExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                AssetCategoryId = assetCategoryId,
                IsMandatory = true,
                Quantity = 2
            },
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profile.Id, result.Value!.PositionProfileId);
        Assert.Equal(assetCategoryId, result.Value.AssetCategoryId);
        Assert.True(result.Value.IsMandatory);
        Assert.Equal(2, result.Value.Quantity);

        var saved = await context.PositionProfileRequiredAssets.SingleAsync();
        Assert.Equal(profile.Id, saved.PositionProfileId);
        Assert.True(saved.IsActive);

        Assert.Single(auditPublisher.Published);
        var auditEvent = auditPublisher.Published[0];
        Assert.Equal("position-profile.required-asset.added", auditEvent.EventType);
        Assert.Equal(profile.Id, auditEvent.EntityId);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher(), assetCategoryExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                AssetCategoryId = Guid.NewGuid()
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
        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), assetCategoryExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
                AssetCategoryId = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AssetCategory_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), assetCategoryExists: false);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                AssetCategoryId = Guid.NewGuid()
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_AssetCategory_Already_Required()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assetCategoryId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var existing = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profile.Id, assetCategoryId, true, 1, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), assetCategoryExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                AssetCategoryId = assetCategoryId,
                IsMandatory = false
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_AssetCategory_On_Different_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assetCategoryId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var existing = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(), companyId, profileA.Id, assetCategoryId, true, 1, Guid.NewGuid(), Now);
        context.PositionProfileRequiredAssets.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher(), assetCategoryExists: true);

        var result = await handler.HandleAsync(
            new AddRequiredAssetRequest
            {
                CompanyId = companyId,
                PositionProfileId = profileB.Id,
                AssetCategoryId = assetCategoryId
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static AddRequiredAssetHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher auditPublisher,
        bool assetCategoryExists)
    {
        return new AddRequiredAssetHandler(
            context,
            new StubAssetCategoryReader(assetCategoryExists),
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

    private sealed class StubAssetCategoryReader(bool exists) : IAssetCategoryReader
    {
        public Task<bool> ExistsAsync(Guid companyId, Guid assetCategoryId, CancellationToken cancellationToken)
            => Task.FromResult(exists);

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> assetCategoryIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
