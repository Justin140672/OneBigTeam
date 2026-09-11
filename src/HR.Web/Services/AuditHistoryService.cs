using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AuditHistoryService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<IReadOnlyList<AuditHistoryItemModel>> GetEmployeeAuditHistoryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GetEmployeeAuditHistoryResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/audit-history",
                HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }
}
