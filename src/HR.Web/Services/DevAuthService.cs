using System.Net.Http.Json;

namespace HR.Web.Services;

public sealed record DevPersonaDto(string UserId, string Name, string JobTitle, string Email);

public sealed class DevAuthService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<IReadOnlyList<DevPersonaDto>> GetPersonasAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<IReadOnlyList<DevPersonaDto>>("api/dev/personas") ?? [];
        }
        catch { return []; }
    }

    public async Task<bool> SwitchAsync(string userId)
    {
        try
        {
            var response = await Http.PostAsync($"api/dev/persona/{userId}", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
