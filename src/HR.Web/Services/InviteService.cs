namespace HR.Web.Services;

public class InviteService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<(bool Success, string? Error)> AcceptInviteAsync(string token, string password)
    {
        var response = await Http.PostAsJsonAsync("api/invites/accept", new { token, password });

        if (response.IsSuccessStatusCode)
            return (true, null);

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return (false, body?.Error ?? "Failed to accept invite.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
