using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Admin.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Admin.Web.Tests;

// Ticket 19 follow-up (P2): verifies that FormText normalization is applied to the JSON body
// sent over the wire by AdminUsersService methods identified as a gap in the general
// consistency pass — email/role/reason values were previously posted unnormalized. Mirrors the
// request-capturing harness used by FormTextSearchNormalizationTests.
public class FormTextBodyNormalizationTests
{
    private sealed class BodyCapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        }
    }

    private static (AdminUsersService Service, BodyCapturingHandler Handler) Build()
    {
        var handler = new BodyCapturingHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var clientFactory = new HrApiHttpClientFactory(provider.GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
        return (new AdminUsersService(clientFactory), handler);
    }

    private static JsonElement ParseBody(string? body)
    {
        Assert.NotNull(body);
        return JsonDocument.Parse(body!).RootElement;
    }

    [Fact]
    public async Task CreateAdministratorAsync_Trims_Email_And_Role_In_Posted_Body()
    {
        var (service, handler) = Build();

        await service.CreateAdministratorAsync("  admin@example.com  ", "  PlatformOwner  ");

        var body = ParseBody(handler.CapturedBody);
        Assert.Equal("admin@example.com", body.GetProperty("email").GetString());
        Assert.Equal("PlatformOwner", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AssignRoleAsync_Trims_Role_In_Posted_Body()
    {
        var (service, handler) = Build();

        await service.AssignRoleAsync(Guid.NewGuid(), "  PlatformSupport  ");

        var body = ParseBody(handler.CapturedBody);
        Assert.Equal("PlatformSupport", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ResetMfaAsync_Trims_Reason_In_Posted_Body()
    {
        var (service, handler) = Build();

        await service.ResetMfaAsync(Guid.NewGuid(), "  lost authenticator device  ");

        var body = ParseBody(handler.CapturedBody);
        Assert.Equal("lost authenticator device", body.GetProperty("reason").GetString());
    }
}
