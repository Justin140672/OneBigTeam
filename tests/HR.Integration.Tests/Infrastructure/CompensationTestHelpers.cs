using System.Net.Http.Json;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Shared helper for the Compensation endpoint tests (Create/Update/Delete FutureCompensationRecord,
/// GetCurrentCompensation, GetCompensationHistory) — every one of those endpoints requires an
/// existing Employee, so this centralizes the "seed reference data, then POST an employee" dance
/// rather than duplicating it across five test classes.
/// </summary>
internal static class CompensationTestHelpers
{
    public static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (employeeId, _) = await CreateEmployeeWithNumberAsync(client, companyId);
        return employeeId;
    }

    /// <summary>
    /// Creates an employee and returns both its ID and EmployeeNumber — needed by the bulk/import
    /// compensation endpoint tests, which key rows on EmployeeNumber (Import) or EmployeeId (Bulk).
    /// </summary>
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
