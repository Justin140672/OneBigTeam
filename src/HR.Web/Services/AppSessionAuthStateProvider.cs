using System.Net.Http.Json;
using System.Security.Claims;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace HR.Web.Services;

public sealed class AppSessionAuthStateProvider(IHttpClientFactory httpClientFactory)
    : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var http     = httpClientFactory.CreateClient("hrapi");
            var response = await http.GetAsync("api/me");

            if (!response.IsSuccessStatusCode)
                return Anonymous;

            var me = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (me is null)
                return Anonymous;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, me.UserId.ToString()),
                new Claim(ClaimTypes.Email,          me.Email ?? string.Empty),
                new Claim("company_id",              me.CompanyId.ToString()),
            };

            var identity = new ClaimsIdentity(claims, authenticationType: "hrapi");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Anonymous;
        }
    }
}
