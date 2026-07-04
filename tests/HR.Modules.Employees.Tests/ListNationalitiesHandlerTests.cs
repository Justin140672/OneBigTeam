using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListNationalities;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListNationalitiesHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Nationalities_Ordered_By_Name()
    {
        await using var context = BuildContext();
        context.Nationalities.AddRange(
            Nationality.Create(1, "British"),
            Nationality.Create(2, "American"),
            Nationality.Create(3, "Canadian"));
        await context.SaveChangesAsync();

        var handler = new ListNationalitiesHandler(context);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(["American", "British", "Canadian"], result.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Nationalities_Seeded()
    {
        await using var context = BuildContext();
        var handler = new ListNationalitiesHandler(context);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
