using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetPositionProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_PositionProfile_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Software Engineer", "Builds stuff", false, null, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);

        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profile.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Software Engineer", result.Value.Title);
        Assert.Equal("Builds stuff", result.Value.Description);
        Assert.False(result.Value.IsManagerial);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetPositionProfileHandler(context);

        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Software Engineer", null, false, null, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);

        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = Guid.NewGuid(), Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_SalaryType_When_Set()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Software Engineer", null, false, null, null, null, 40000, 60000, SalaryType.Daily, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);

        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalaryType.Daily, result.Value!.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_SalaryType_When_Not_Set()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Software Engineer", null, false, null, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new GetPositionProfileHandler(context);

        var result = await handler.HandleAsync(
            new GetPositionProfileRequest { CompanyId = companyId, Id = profile.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SalaryType);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
