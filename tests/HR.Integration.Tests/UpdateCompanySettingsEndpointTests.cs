using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;

namespace HR.Integration.Tests;

public class UpdateCompanySettingsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
	private readonly ApiWebApplicationFactory _factory;

	public UpdateCompanySettingsEndpointTests(ApiWebApplicationFactory factory)
	{
		_factory = factory;
	}

	[Fact]
	public async Task Put_Company_Settings_Returns_Unauthorized_For_Anonymous_Request()
	{
		using var client = _factory.CreateClient();

		var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
		{
			timeZone = "UTC",
			locale = "en-GB",
			workingWeek = (int)(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday),
			leaveYearStartMonth = 1,
			defaultHolidayAllowance = 25.0m,
			probationMonths = 6
		});

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Put_Company_Settings_Updates_Settings_For_Authenticated_Request()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-7");

		var createResponse = await client.PostAsJsonAsync("/api/companies", new
		{
			name = $"Settings Test {Guid.NewGuid():N}"
		});
		createResponse.EnsureSuccessStatusCode();

		var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
		Assert.NotNull(createdCompany);

		var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}/settings", new
		{
			timeZone = "Europe/London",
			locale = "en-GB",
			workingWeek = (int)(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday),
			leaveYearStartMonth = 4,
			defaultHolidayAllowance = 28.5m,
			probationMonths = 3
		});

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
		Assert.NotNull(payload);
		Assert.Equal(createdCompany.Id, payload!.CompanyId);
		Assert.Equal("Europe/London", payload.TimeZone);
		Assert.Equal("en-GB", payload.Locale);
		Assert.Equal(
			WorkingDays.Monday
			| WorkingDays.Tuesday
			| WorkingDays.Wednesday
			| WorkingDays.Thursday
			| WorkingDays.Friday,
			payload.WorkingWeek);
		Assert.Equal(4, payload.LeaveYearStartMonth);
		Assert.Equal(28.5m, payload.DefaultHolidayAllowance);
		Assert.Equal(3, payload.ProbationMonths);
	}

	private sealed record CreateCompanyPayload(
		Guid Id,
		string Name,
		string Slug,
		bool IsActive,
		DateTimeOffset CreatedAt);

	private sealed record UpdateCompanySettingsPayload(
		Guid CompanyId,
		string TimeZone,
		string Locale,
		WorkingDays WorkingWeek,
		int LeaveYearStartMonth,
		decimal DefaultHolidayAllowance,
		int ProbationMonths,
		DateTimeOffset UpdatedAt);
}
