using System.Net.Http.Json;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class WorkEmailTestHelper
{
    public static async Task ConfigureAsync(
        HttpClient client,
        Guid companyId,
        bool enabled = true,
        string? primaryDomain = "example.com",
        string[]? additionalDomains = null,
        string convention = "FirstNameDotLastName")
    {
        var current = await client.GetFromJsonAsync<VersionPayload>($"/api/companies/{companyId}/work-email-settings");

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/work-email-settings", new
        {
            suggestionsEnabled = enabled,
            primaryDomain,
            additionalDomains = additionalDomains ?? [],
            namingConvention = convention,
            version = current!.Version,
        });
        response.EnsureSuccessStatusCode();
    }

    public static async Task AddEmployeeAsync(ApiWebApplicationFactory factory, Guid companyId, string workEmail)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);

        db.Employees.Add(Employee.Create(
            Guid.NewGuid(), companyId, "Existing", "Employee", workEmail, new DateOnly(2026, 1, 1),
            hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId,
            refData.PositionProfileId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private sealed record VersionPayload(int Version);
}
