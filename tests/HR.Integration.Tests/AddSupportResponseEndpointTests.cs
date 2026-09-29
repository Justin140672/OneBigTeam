using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel.Html;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AddSupportResponseEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = Guid.Parse("60000000-0000-0000-0000-000000000007");

    public AddSupportResponseEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static MultipartFormDataContent BuildSubmission(Guid companyId, string title) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent("AskQuestion"), "Type" },
        { new StringContent(title), "Title" },
        { new StringContent("Some description of the issue."), "Description" },
        { new StringContent("Low"), "Priority" },
        { new StringContent("false"), "IncludeDiagnostics" },
    };

    private static MultipartFormDataContent BuildResponse(Guid companyId, Guid id, string bodyHtml) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent(id.ToString()), "Id" },
        { new StringContent(bodyHtml), "BodyHtml" },
    };

    [Fact]
    public async Task Post_SupportResponse_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponse(Guid.NewGuid(), Guid.NewGuid(), "Reply"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportResponse_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponse(companyId, Guid.NewGuid(), "Reply"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportResponse_From_Staff_Is_Flagged_As_Staff()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);

        var created = await adminClient.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Staff response issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(payload);

        var response = await adminClient.PostAsync(
            $"/api/companies/{companyId}/support/requests/{payload!.Id}/responses",
            BuildResponse(companyId, payload.Id, "We're looking into it."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responsePayload = await response.Content.ReadFromJsonAsync<ResponsePayload>();
        Assert.NotNull(responsePayload);
        Assert.True(responsePayload!.IsStaffResponse);
    }


    private const string MaliciousBody =
        "<p>Hi <strong>there</strong></p><script>alert(1)</script><img src=x onerror=alert(1)>" +
        "<a href=\"javascript:alert(1)\">x</a><iframe src=\"https://evil.example\"></iframe>" +
        "<a href=\"https://example.com/help\">help</a>";

    private static void AssertSanitised(string bodyHtml)
    {
        Assert.DoesNotContain("<script", bodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", bodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", bodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", bodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", bodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<strong>there</strong>", bodyHtml);
        Assert.Contains("href=\"https://example.com/help\"", bodyHtml);
        Assert.Contains($"rel=\"{SupportHtmlSanitizer.LinkRel}\"", bodyHtml);
    }

    private async Task<Guid> SubmitRequestAsync(HttpClient client, Guid companyId, string title)
    {
        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, title));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    [Fact]
    public async Task Post_SupportResponse_Persists_Sanitised_Html()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);
        var requestId = await SubmitRequestAsync(adminClient, companyId, "Sanitised response issue");

        var response = await adminClient.PostAsync(
            $"/api/companies/{companyId}/support/requests/{requestId}/responses",
            BuildResponse(companyId, requestId, MaliciousBody));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responsePayload = await response.Content.ReadFromJsonAsync<ResponsePayload>();
        Assert.NotNull(responsePayload);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            var saved = await db.SupportResponses.AsNoTracking().SingleAsync(r => r.Id == responsePayload!.Id);
            AssertSanitised(saved.BodyHtml);
            Assert.Equal(SupportHtmlSanitizer.Sanitize(MaliciousBody), saved.BodyHtml);
            Assert.Equal(companyId, saved.CompanyId);
        }

        var detail = await adminClient.GetAsync($"/api/companies/{companyId}/support/requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailPayload = await detail.Content.ReadFromJsonAsync<SupportRequestDetailPayload>();
        Assert.NotNull(detailPayload);
        var thread = Assert.Single(detailPayload!.Responses);
        Assert.Equal(responsePayload!.Id, thread.Id);
        AssertSanitised(thread.BodyHtml);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)><iframe src=\"javascript:alert(1)\"></iframe><svg onload=alert(1)></svg>")]
    [InlineData("<script></script>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Post_SupportResponse_Returns_BadRequest_When_Body_Has_No_Permitted_Content(string body)
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);
        var requestId = await SubmitRequestAsync(adminClient, companyId, "Empty sanitised response issue");

        var response = await adminClient.PostAsync(
            $"/api/companies/{companyId}/support/requests/{requestId}/responses",
            BuildResponse(companyId, requestId, body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        Assert.False(await db.SupportResponses.AnyAsync(r => r.SupportRequestId == requestId));
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);
    private sealed record ResponsePayload(Guid Id, bool IsStaffResponse, DateTimeOffset CreatedAt);
    private sealed record SupportRequestDetailPayload(Guid Id, List<SupportResponseDtoPayload> Responses);
    private sealed record SupportResponseDtoPayload(Guid Id, bool IsStaffResponse, string BodyHtml);
}
