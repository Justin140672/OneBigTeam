using HR.Web.Models;

namespace HR.Web.Services;

public sealed class LeaveService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<LeaveBalanceResponse?> GetEmployeeLeaveBalanceAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<LeaveBalanceResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-balances",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
