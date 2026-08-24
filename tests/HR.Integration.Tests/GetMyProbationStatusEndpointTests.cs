using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Proves GetMyProbationStatus is reachable by a plain, authenticated Employee — resolving the
/// employee purely from the caller's own "sub" claim — unlike the HR-only
/// GetProbationRecordByEmployee endpoint ("probation:manage") which 403s a real employee viewing
/// their own profile.
/// </summary>
[Collection("Integration")]
public class GetMyProbationStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SelfUser = new("cc00000a-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("cc00000a-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetMyProbationStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, SelfUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    [Fact]
    public async Task Get_MyProbationStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/me/probation-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_MyProbationStatus_Returns_HasRecord_False_When_No_Record_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(SelfUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasRecord);
    }

    [Fact]
    public async Task Get_MyProbationStatus_Returns_Ok_With_Own_Record_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(SelfUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
            db.ProbationRecords.Add(ProbationRecord.Create(
                Guid.NewGuid(), companyId, SelfUser, Guid.NewGuid(),
                new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), "Self-service coverage.", DateOnly.FromDateTime(Now.UtcDateTime), Now));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasRecord);
        Assert.Equal("Active", payload.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), payload.StartDate);
    }

    [Fact]
    public async Task Get_MyProbationStatus_Does_Not_Return_Another_Employees_Record()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(SelfUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
            db.ProbationRecords.Add(ProbationRecord.Create(
                Guid.NewGuid(), companyId, OtherUser, Guid.NewGuid(),
                new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/probation-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasRecord);
    }

    private sealed record StatusPayload(
        bool HasRecord, Guid? Id, DateOnly? StartDate, DateOnly? ExpectedEndDate,
        string? Status, DateOnly? DecisionDate, string? OutcomeNotes);
}
