using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Derive Vacancy Role Information from Position Profile" story's read-only
/// "Linked Position Profile" card on the Vacancy detail page (data-testid=
/// "linked-position-profile-card" in VacancyDetail.razor's RenderDetailsCard) and the
/// corresponding "Position Profile" column on the Vacancy list grid (VacancyList.razor).
///
/// See VacancyPositionProfileDefaultsTests for the sibling coverage of the CREATE form's
/// Position-Profile-driven defaults (the "From Position Profile" summary card / auto-populated
/// Department) that this read-only detail-page card is the natural follow-on to. As there, vacancy
/// creation (recruitment:manage) is Recruiter-only (Marcus Diallo) while Position Profile creation
/// (infra:manage) belongs to an HR Administrator (Laura Bennett) — tests switch accounts via
/// LoginPage.SwitchAccountAsync as needed.
/// </summary>
[Collection("E2E")]
public sealed class VacancyLinkedPositionProfileTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task ViewingVacancy_ShowsLinkedPositionProfileDetails_SourcedFromProfileNotVacancy()
    {
        var profileTitle = $"E2E Linked Profile {Guid.NewGuid().ToString("N")[..8]}";
        var profileDescription = $"E2E profile description {Guid.NewGuid():N}";
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        // Seed a Position Profile with its own distinct Title/Department/Description, independent
        // of anything the vacancy itself will be given below, so the assertions can prove the
        // "Linked Position Profile" card is genuinely sourced from the profile rather than
        // coincidentally mirroring the vacancy's own fields.
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        // Department, Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.FillDescriptionAsync(profileDescription);
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        // Create a vacancy linked to that profile, giving the vacancy its own distinct Advert
        // Title. (Selecting a Position Profile no longer auto-populates Advert Title as of the
        // "Refactor Duplicate Vacancy Fields" story, so fill order no longer matters here — but
        // filling it before selecting the profile still exercises the same happy path as before.)
        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsLinkedPositionProfileCardVisibleAsync(),
            "Expected the 'Linked Position Profile' card to render for an existing vacancy");

        Assert.Equal(profileTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Contains("Engineering", await vacancyDetail.GetLinkedPositionProfileDepartmentAsync() ?? string.Empty);
        Assert.Equal(profileDescription, await vacancyDetail.GetLinkedPositionProfileDescriptionAsync());

        // The vacancy's own Title (Recruitment Advert Details card) genuinely differs from the
        // linked profile's Title, proving the card isn't just echoing the vacancy's own field.
        Assert.NotEqual(vacancyTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Equal(vacancyTitle, await vacancyDetail.GetTitleAsync());

        // No "Inactive" indicator for a still-active profile.
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

    /// <summary>
    /// Intended to verify that when a vacancy's linked Position Profile has since been
    /// deactivated, the "Linked Position Profile" card still renders the profile's
    /// Title/Department/Description and additionally shows an "Inactive" indicator (see
    /// ActiveStatusBadge usage gated on "_vacancy.PositionProfileIsActive == false" in
    /// VacancyDetail.razor's RenderDetailsCard).
    ///
    /// Position Profile deactivation is now available via a "Deactivate" toolbar action on
    /// PositionProfileList.razor, backed by DELETE
    /// /api/companies/{companyId}/position-profiles/{id} (see
    /// PositionProfileListPage.DeactivateAsync). This test seeds/creates a position profile, links
    /// a vacancy to it, deactivates the profile, then reloads the vacancy's detail page and asserts
    /// IsLinkedPositionProfileInactiveBadgeVisibleAsync() is true while Title/Department/Description
    /// are unchanged.
    /// </summary>
    [Fact]
    public async Task ViewingVacancy_WithDeactivatedLinkedProfile_ShowsInactiveIndicator()
    {
        var profileTitle = $"E2E Deactivated Linked Profile {Guid.NewGuid().ToString("N")[..8]}";
        var profileDescription = $"E2E profile description {Guid.NewGuid():N}";
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
        // Department, Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.FillDescriptionAsync(profileDescription);
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        // Link a vacancy to the still-active profile before deactivating it.
        await login.SwitchAccountAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        // Deactivate the linked profile.
        await login.SwitchAccountAsync(LauraEmail);
        await ppList.GoToAsync(AcmeId);
        await ppList.DeactivateAsync(profileTitle);

        // Reload the vacancy's detail page and confirm the linked profile card shows the
        // "Inactive" indicator while its Title/Department/Description remain unchanged.
        await login.SwitchAccountAsync(MarcusEmail);
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsLinkedPositionProfileCardVisibleAsync(),
            "Expected the 'Linked Position Profile' card to render for an existing vacancy");
        Assert.Equal(profileTitle, await vacancyDetail.GetLinkedPositionProfileTitleAsync());
        Assert.Contains("Engineering", await vacancyDetail.GetLinkedPositionProfileDepartmentAsync() ?? string.Empty);
        Assert.Equal(profileDescription, await vacancyDetail.GetLinkedPositionProfileDescriptionAsync());

        Assert.True(await vacancyDetail.IsLinkedPositionProfileInactiveBadgeVisibleAsync(),
            "Expected an 'Inactive' indicator for a deactivated linked position profile");
    }
}
