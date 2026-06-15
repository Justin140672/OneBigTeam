using System.Net.Http.Json;

namespace HR.Web.Services;

public sealed record DevPersonaDto(string UserId, string Name, string JobTitle, string Email);

public sealed class DevService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<IReadOnlyList<DevPersonaDto>> GetPersonasAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<List<DevPersonaDto>>("api/dev/personas", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    public async Task SwitchPersonaAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Http.PostAsync($"api/dev/persona/{userId}", null, cancellationToken);
        }
        catch { }
    }
}
