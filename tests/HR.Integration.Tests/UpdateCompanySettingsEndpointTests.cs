using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

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
			workingDays = 31,
			hoursPerDay = 7.5,
			leaveYearStartMonth = 1,
			defaultHolidayAllowance = 25,
			probationMonths = 6
		});

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Put_Company_Settings_Updates_Settings_For_Authenticated_Request()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-7");
		client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-7");

		var createResponse = await client.PostAsJsonAsync("/api/companies", new
		{
			name = $"Settings Test {Guid.NewGuid():N}",
			addresses = new[]
			{
				new
				{
					type = "RegisteredOffice",
					line1 = "10 High Street",
					city = "London",
					countryCode = "GB"
				}
			}
		});
		createResponse.EnsureSuccessStatusCode();

		var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
		Assert.NotNull(createdCompany);

		var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}/settings", new
		{
			timeZone = "Europe/London",
			locale = "en-GB",
			workingDays = 31,
			hoursPerDay = 7.5,
			leaveYearStartMonth = 4,
			defaultHolidayAllowance = 28,
			probationMonths = 3,
		});

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
		Assert.NotNull(payload);
		Assert.Equal(createdCompany.Id, payload!.CompanyId);
		Assert.Equal("Europe/London", payload.TimeZone);
		Assert.Equal(28, payload.DefaultHolidayAllowance);
	}

	[Fact]
	public async Task Put_Company_Settings_Returns_NotFound_For_Unknown_Id()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-8");
		client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-8");

		var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
		{
			timeZone = "UTC",
			locale = "en-GB",
			workingDays = 31,
			hoursPerDay = 7.5,
			leaveYearStartMonth = 1,
			defaultHolidayAllowance = 25,
			probationMonths = 6,
		});

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	private sealed record CreateCompanyPayload(Guid Id);

	private sealed record UpdateCompanySettingsPayload(
		Guid CompanyId,
		string TimeZone,
		string Locale,
		string WorkingDays,
		decimal HoursPerDay,
		int LeaveYearStartMonth,
		decimal DefaultHolidayAllowance,
		int ProbationMonths,
		bool ExcludePublicHolidaysFromLeave,
		DateTimeOffset UpdatedAt);
}
