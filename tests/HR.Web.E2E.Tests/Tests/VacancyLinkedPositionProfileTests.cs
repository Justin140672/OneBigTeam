using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VacancyLinkedPositionProfileTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task ViewingVacancy_ShowsLinkedPositionProfileDetails_SourcedFromProfileNotVacancy()
    {
        var profileTitle = $"E2E Linked Profile {Guid.NewGuid().ToString("N")[..8]}";
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsLinkedPositionProfileCardVisibleAsync(),
            "Expected the 'Linked Position Profile' card to render for an existing vacancy");

        Assert.Equal(profileTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Contains("Engineering", await vacancyDetail.GetLinkedPositionProfileDepartmentAsync() ?? string.Empty);

        Assert.NotEqual(vacancyTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Equal(vacancyTitle, await vacancyDetail.GetTitleAsync());

        Assert.False(await vacancyDetail.IsLinkedPositionProfileInactiveBadgeVisibleAsync(),
            "Did not expect an 'Inactive' indicator for a still-active linked position profile");
    }

    [Fact]
    public async Task VacancyList_ShowsPositionProfileColumn_ForSeededVacancy()
    {
        // "HR Business Partner" is seeded linked to the "HR Advisor" position profile — a
        // deliberately different title from the vacancy's own (see RecruitmentModule's seed
        // comment: "no 'HR Business Partner' profile exists, so this is a manual assignment
        // rather than an automatic exact-title match"), which conveniently also proves the list
        // column reflects the linked profile's own title rather than the vacancy's own title.
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        Assert.Equal("HR Advisor",
            await vacancyList.GetPositionProfileColumnTextAsync("HR Business Partner"));
    }

    [Fact]
    public async Task ViewingVacancy_WithDeactivatedLinkedProfile_ShowsInactiveIndicator()
    {
        var profileTitle = $"E2E Deactivated Linked Profile {Guid.NewGuid().ToString("N")[..8]}";
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        await login.SwitchAccountAsync(LauraEmail);
        await ppList.GoToAsync(AcmeId);
        await ppList.DeactivateAsync(profileTitle);

        await login.SwitchAccountAsync(MarcusEmail);
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsLinkedPositionProfileCardVisibleAsync(),
            "Expected the 'Linked Position Profile' card to render for an existing vacancy");
        Assert.Equal(profileTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Contains("Engineering", await vacancyDetail.GetLinkedPositionProfileDepartmentAsync() ?? string.Empty);

        Assert.True(await vacancyDetail.IsLinkedPositionProfileInactiveBadgeVisibleAsync(),
            "Expected an 'Inactive' indicator for a deactivated linked position profile");
    }
}
