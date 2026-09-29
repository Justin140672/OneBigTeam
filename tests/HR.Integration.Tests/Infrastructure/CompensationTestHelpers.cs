using System.Net.Http.Json;

namespace HR.Integration.Tests.Infrastructure;

internal static class CompensationTestHelpers
{
    public static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (employeeId, _) = await CreateEmployeeWithNumberAsync(client, companyId);
        return employeeId;
    }

    public static async Task<(Guid EmployeeId, string EmployeeNumber)> CreateEmployeeWithNumberAsync(
        HttpClient client, Guid companyId, string? firstName = "Comp", string? lastName = "Tester")
    {
        var referenceData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var employeeNumber = $"EMP-{Guid.NewGuid():N}";

        var request = EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId,
            referenceData,
            firstName: firstName ?? "Comp",
            lastName: lastName ?? "Tester",
            workEmail: $"comp.tester.{Guid.NewGuid():N}@example.com",
            employeeNumber: employeeNumber);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return (payload!.Id, employeeNumber);
    }

    private sealed record IdPayload(Guid Id);
}
