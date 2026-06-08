using System.Net.Http.Json;

namespace HR.Web.Services;

public class InviteService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<(string? Token, DateTimeOffset ExpiresAt, string? Error)> SendInviteAsync(
        Guid companyId, Guid employeeId, string email)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/invite",
            new { companyId, employeeId, email });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<InviteResponse>();
            return (result?.Token, result?.ExpiresAt ?? default, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return (null, default, "You do not have permission to send invites.");

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return (null, default, body?.Error ?? "Failed to send invite.");
    }

    public async Task<(bool Success, string? Error)> AcceptInviteAsync(string token, string password)
    {
        var response = await Http.PostAsJsonAsync("api/invites/accept", new { token, password });

        if (response.IsSuccessStatusCode)
            return (true, null);

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return (false, body?.Error ?? "Failed to accept invite.");
    }

    private sealed record InviteResponse(string Token, DateTimeOffset ExpiresAt);
    private sealed record ErrorEnvelope(string? Error);
}
