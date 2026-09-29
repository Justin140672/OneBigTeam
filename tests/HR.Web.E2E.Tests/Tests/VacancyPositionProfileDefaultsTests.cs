using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VacancyPositionProfileDefaultsTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task SelectingPositionProfile_ShowsDefaultsSummaryCard()
    {
        var profileTitle = $"E2E Profile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.FillSalaryRangeAsync(50000, 70000);
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);

        Assert.True(await vacancyDetail.IsPositionProfileDefaultsSummaryVisibleAsync(),
            "Expected the 'From Position Profile' summary card to appear once a profile is selected");

        Assert.Contains("Engineering", await vacancyDetail.GetSummaryDepartmentNameAsync() ?? string.Empty);
        Assert.Contains("50,000", await vacancyDetail.GetSummarySalaryRangeAsync() ?? string.Empty);

    }

    [Fact]
    public async Task CreateVacancy_DepartmentFieldIsAbsentFromAdvertDetailsCard()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        Assert.Equal(0, await vacancyDetail.CountDepartmentFieldsInAdvertDetailsCardAsync());
    }

    [Fact]
    public async Task CreateVacancy_PositionProfileDropdown_OnlyShowsActiveProfiles()
    {
        var activeProfileTitle   = $"E2E Active Profile {Guid.NewGuid().ToString("N")[..8]}";
        var inactiveProfileTitle = $"E2E Inactive Profile {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(activeProfileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();
        Assert.True(await ppList.HasPositionProfileAsync(activeProfileTitle),
            $"Expected the new position profile '{activeProfileTitle}' to appear in the list");

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(inactiveProfileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();
        Assert.True(await ppList.HasPositionProfileAsync(inactiveProfileTitle),
            $"Expected the new position profile '{inactiveProfileTitle}' to appear in the list");

        await ppList.GoToAsync(AcmeId);
        await ppList.DeactivateAsync(inactiveProfileTitle);

        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.OpenPositionProfileDropdownAsync();
        var options = await vacancyDetail.GetPositionProfileDropdownOptionsAsync();

        Assert.Contains(activeProfileTitle, options);
        Assert.DoesNotContain(inactiveProfileTitle, options);
    }
}
