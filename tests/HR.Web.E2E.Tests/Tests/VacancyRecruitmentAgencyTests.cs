using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the optional "Recruitment Agency" dropdown on the vacancy form (ticket #81/#94) —
/// VacancyDetail.razor's SfDropDownList bound to Model.AssignedRecruiterId, a FK to
/// ExternalRecruiter (not the removed VacancyRecruiterAssignment/Recruiters-tab feature). The
/// DataSource is active-recruiters-only, with a prepended "Not assigned" sentinel item rather
/// than Syncfusion's ShowClearButton.
/// </summary>
public sealed class VacancyRecruitmentAgencyTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task CreateVacancy_SelectsActiveRecruitmentAgency_PersistsAcrossReload()
    {
        var agencyName    = $"E2E Agency {Guid.NewGuid().ToString("N")[..8]}";
        var vacancyTitle  = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login             = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList     = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterDetail   = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
        var vacancyList       = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail     = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Seed an active external recruiter (agency) with a unique name.
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterDetail.FillAgencyNameAsync(agencyName);
        await recruiterDetail.SaveAsync();
        Assert.True(await recruiterList.HasItemAsync(agencyName),
            $"Expected the new agency '{agencyName}' to appear in the list");

        // Create a vacancy and assign that agency via the Recruitment Agency dropdown.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectRecruitmentAgencyAsync(agencyName);
        await vacancyDetail.SaveNewVacancyAsync();

        // Reopen the vacancy and confirm the assignment persisted.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.Contains(agencyName, await vacancyDetail.GetSelectedRecruitmentAgencyTextAsync() ?? string.Empty);
    }

    [Fact]
    public async Task VacancyForm_RecruitmentAgencyDropdown_ExcludesInactiveAgencies()
    {
        var activeAgencyName   = $"E2E Active Agency {Guid.NewGuid().ToString("N")[..8]}";
        var inactiveAgencyName = $"E2E Inactive Agency {Guid.NewGuid().ToString("N")[..8]}";

        var login           = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList   = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterDetail = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail   = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Seed one active and one (subsequently deactivated) agency.
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterDetail.FillAgencyNameAsync(activeAgencyName);
        await recruiterDetail.SaveAsync();

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterDetail.FillAgencyNameAsync(inactiveAgencyName);
        await recruiterDetail.SaveAsync();
        await recruiterList.DeactivateAsync(inactiveAgencyName);
        Assert.False(await recruiterList.IsActiveAsync(inactiveAgencyName),
            $"Expected '{inactiveAgencyName}' to be deactivated before checking the vacancy form's dropdown");

        // On the vacancy create form, the Recruitment Agency dropdown must list the active agency
        // but never the deactivated one.
        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.OpenRecruitmentAgencyDropdownAsync();
        var options = await vacancyDetail.GetRecruitmentAgencyDropdownOptionsAsync();

        Assert.Contains(options, o => o.Contains(activeAgencyName));
        Assert.DoesNotContain(options, o => o.Contains(inactiveAgencyName));
    }
}
