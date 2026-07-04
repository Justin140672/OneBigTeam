using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListEmploymentTypes;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListEmploymentTypesHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_All_EmploymentTypes_Ordered_By_Name_When_IsActive_Filter_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var fullTime = EmploymentType.Create(Guid.NewGuid(), companyId, "Full Time", null, now);
        var partTime = EmploymentType.Create(Guid.NewGuid(), companyId, "Part Time", null, now);
        partTime.Deactivate(now);
        context.EmploymentTypes.AddRange(fullTime, partTime);
        await context.SaveChangesAsync();

        var handler = new ListEmploymentTypesHandler(context);
        var result = await handler.HandleAsync(new ListEmploymentTypesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(["Full Time", "Part Time"], result.Value.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task HandleAsync_Filters_By_IsActive_When_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var active = EmploymentType.Create(Guid.NewGuid(), companyId, "Full Time", null, now);
        var inactive = EmploymentType.Create(Guid.NewGuid(), companyId, "Part Time", null, now);
        inactive.Deactivate(now);
        context.EmploymentTypes.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListEmploymentTypesHandler(context);
        var result = await handler.HandleAsync(
            new ListEmploymentTypesRequest { CompanyId = companyId, IsActive = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Full Time", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.EmploymentTypes.AddRange(
            EmploymentType.Create(Guid.NewGuid(), companyA, "Full Time", null, now),
            EmploymentType.Create(Guid.NewGuid(), companyB, "Contractor", null, now));
        await context.SaveChangesAsync();

        var handler = new ListEmploymentTypesHandler(context);
        var result = await handler.HandleAsync(new ListEmploymentTypesRequest { CompanyId = companyA }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Full Time", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_EmploymentTypes_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListEmploymentTypesHandler(context);

        var result = await handler.HandleAsync(new ListEmploymentTypesRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
