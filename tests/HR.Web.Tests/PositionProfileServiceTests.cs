using System.Net;
using System.Net.Http.Json;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class PositionProfileServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task GetPositionProfileAsync_Returns_New_Template_Fields_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var leavePolicyId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var response = new GetPositionProfileResponse(
            profileId, companyId, departmentId, locationId, "Senior Developer", null,
            ProbationMonthsOverride: 3,
            WorkingDaysOverride: WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
            HoursPerDayOverride: 6m,
            SalaryMin: 40000m,
            SalaryMax: 60000m,
            SalaryType: "Annual",
            DefaultLeavePolicyId: leavePolicyId,
            OnboardingTemplateId: null,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            RequiredDocuments: [],
            RequiredAssets: []);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new PositionProfileService(factory);

        var result = await service.GetPositionProfileAsync(companyId, profileId);

        Assert.NotNull(result);
        Assert.Equal(3, result.ProbationMonthsOverride);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, result.WorkingDaysOverride);
        Assert.Equal(6m, result.HoursPerDayOverride);
        Assert.Equal(40000m, result.SalaryMin);
        Assert.Equal(60000m, result.SalaryMax);
        Assert.Equal("Annual", result.SalaryType);
        Assert.Equal(leavePolicyId, result.DefaultLeavePolicyId);
        Assert.Equal(locationId, result.LocationId);
    }

    [Fact]
    public async Task GetPositionProfileAsync_Returns_Null_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new PositionProfileService(factory);

        var result = await service.GetPositionProfileAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task CreatePositionProfileAsync_Sends_New_Template_Fields()
    {
        var companyId = Guid.NewGuid();
        var leavePolicyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        HttpRequestMessage? captured = null;

        var response = new CreatePositionProfileResponse(
            Guid.NewGuid(), companyId, departmentId, "Senior Developer", null, true, DateTimeOffset.UtcNow);

        var handler = new CapturingJsonResponseHandler(HttpStatusCode.OK, response, req => captured = req);
        var factory = BuildFactory(handler);
        var service = new PositionProfileService(factory);

        var request = new CreatePositionProfileRequest(
            companyId, departmentId, locationId, "Senior Developer", null,
            ProbationMonthsOverride: 3,
            WorkingDaysOverride: WorkingDays.Monday | WorkingDays.Tuesday,
            HoursPerDayOverride: 6m,
            SalaryMin: 40000m,
            SalaryMax: 60000m,
            SalaryType: "Annual",
            DefaultLeavePolicyId: leavePolicyId,
            OnboardingTemplateId: null);

        var (created, error) = await service.CreatePositionProfileAsync(companyId, request);

        Assert.NotNull(created);
        Assert.Null(error);
        Assert.NotNull(captured);

        var sentBody = await captured!.Content!.ReadFromJsonAsync<CreatePositionProfileRequest>();
        Assert.Equal(3, sentBody!.ProbationMonthsOverride);
        Assert.Equal(leavePolicyId, sentBody.DefaultLeavePolicyId);
        Assert.Equal(60000m, sentBody.SalaryMax);
        Assert.Equal("Annual", sentBody.SalaryType);
    }

    [Fact]
    public async Task CreatePositionProfileAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "A position profile with that title already exists." }));
        var service = new PositionProfileService(factory);

        var (created, error) = await service.CreatePositionProfileAsync(
            Guid.NewGuid(), new CreatePositionProfileRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Duplicate", null, null, null, null, null, null, null, Guid.NewGuid(), null));

        Assert.Null(created);
        Assert.Equal("A position profile with that title already exists.", error);
    }

    [Fact]
    public async Task UpdatePositionProfileAsync_Sends_New_Template_Fields()
    {
        var companyId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var leavePolicyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        HttpRequestMessage? captured = null;

        var handler = new CapturingJsonResponseHandler(HttpStatusCode.OK, new { }, req => captured = req);
        var factory = BuildFactory(handler);
        var service = new PositionProfileService(factory);

        var request = new UpdatePositionProfileRequest(
            companyId, profileId, departmentId, locationId, "Senior Developer", null,
            ProbationMonthsOverride: 4,
            WorkingDaysOverride: WorkingDays.Thursday,
            HoursPerDayOverride: 8m,
            SalaryMin: 50000m,
            SalaryMax: 70000m,
            SalaryType: "Annual",
            DefaultLeavePolicyId: leavePolicyId,
            OnboardingTemplateId: null);

        var (success, error) = await service.UpdatePositionProfileAsync(companyId, profileId, request);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(captured);

        var sentBody = await captured!.Content!.ReadFromJsonAsync<UpdatePositionProfileRequest>();
        Assert.Equal(4, sentBody!.ProbationMonthsOverride);
        Assert.Equal(leavePolicyId, sentBody.DefaultLeavePolicyId);
        Assert.Equal(70000m, sentBody.SalaryMax);
        Assert.Equal("Annual", sentBody.SalaryType);
    }

    [Fact]
    public async Task ListPositionProfilesAsync_Maps_SalaryFields()
    {
        var companyId = Guid.NewGuid();

        var response = new ListPositionProfilesResponse(
        [
            new PositionProfileListItemModel(
                Guid.NewGuid(), "Engineering", "Senior Developer", null, true,
                SalaryMin: 40000m, SalaryMax: 60000m, SalaryType: "Annual")
        ]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new PositionProfileService(factory);

        var result = await service.ListPositionProfilesAsync(companyId);

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(40000m, item.SalaryMin);
        Assert.Equal(60000m, item.SalaryMax);
        Assert.Equal("Annual", item.SalaryType);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingJsonResponseHandler(HttpStatusCode statusCode, object payload, Action<HttpRequestMessage> onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest(request);
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
