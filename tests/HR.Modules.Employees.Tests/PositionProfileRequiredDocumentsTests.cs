using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PositionProfileRequiredDocumentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_ActiveRequiredDocuments_With_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var active = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), true, 30, true, Guid.NewGuid(), Now);
        var inactive = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profile.Id, Guid.NewGuid(), false, null, false, Guid.NewGuid(), Now);
        inactive.Deactivate();

        context.PositionProfileRequiredDocuments.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.RequiredDocuments);
        var doc = result.Value.RequiredDocuments[0];
        Assert.Equal(active.Id, doc.Id);
        Assert.Equal(active.DocumentTypeId, doc.DocumentTypeId);
        Assert.True(doc.IsMandatory);
        Assert.Equal(30, doc.DueDaysAfterStart);
        Assert.True(doc.RequiresExpiryDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmptyRequiredDocuments_When_None_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RequiredDocuments);
    }

    [Fact]
    public async Task HandleAsync_Excludes_RequiredDocuments_From_Other_Profiles()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Designer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var docForB = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(), companyId, profileB.Id, Guid.NewGuid(), true, null, false, Guid.NewGuid(), Now);
        context.PositionProfileRequiredDocuments.Add(docForB);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profileA.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RequiredDocuments);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
