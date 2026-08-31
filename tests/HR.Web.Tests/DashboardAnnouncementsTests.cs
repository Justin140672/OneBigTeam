using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

/// <summary>
/// DSH-07: phrasing of the dashboards' visually-hidden polite aria-live announcements
/// (<see cref="DashboardAnnouncements"/>). Pure string builders, no Blazor.
/// </summary>
public class DashboardAnnouncementsTests
{
    [Fact]
    public void LoadComplete_AppendsFinishedLoadingSentence()
    {
        Assert.Equal("Recruitment dashboard finished loading.",
            DashboardAnnouncements.LoadComplete("Recruitment dashboard"));
    }

    [Fact]
    public void Counts_Empty_ReturnsNull()
    {
        Assert.Null(DashboardAnnouncements.Counts([]));
    }

    [Fact]
    public void Counts_SingleItem_RendersCountThenLabelWithTerminatingFullStop()
    {
        Assert.Equal("3 open vacancies.",
            DashboardAnnouncements.Counts([("open vacancies", 3)]));
    }

    [Fact]
    public void Counts_MultipleItems_JoinedWithCommaSpace_AndSingleTerminatingFullStop()
    {
        var result = DashboardAnnouncements.Counts(
        [
            ("open vacancies", 3),
            ("interviews requiring action", 2),
        ]);

        Assert.Equal("3 open vacancies, 2 interviews requiring action.", result);
    }

    [Fact]
    public void Counts_ZeroCount_IsStillRendered()
    {
        Assert.Equal("0 offers awaiting response.",
            DashboardAnnouncements.Counts([("offers awaiting response", 0)]));
    }

    [Fact]
    public void PartialFailure_Empty_ReturnsNull()
    {
        Assert.Null(DashboardAnnouncements.PartialFailure([]));
    }

    [Fact]
    public void PartialFailure_WhitespaceOnlyEntries_AreIgnored_ReturnsNull()
    {
        Assert.Null(DashboardAnnouncements.PartialFailure([" ", "", "   "]));
    }

    [Fact]
    public void PartialFailure_SingleSource_FormatsWithColonAndFullStop()
    {
        Assert.Equal("Some information could not be loaded: Open vacancies.",
            DashboardAnnouncements.PartialFailure(["Open vacancies"]));
    }

    [Fact]
    public void PartialFailure_MultipleSources_JoinedWithCommaSpace_BlanksFilteredOut()
    {
        var result = DashboardAnnouncements.PartialFailure(["Open vacancies", "  ", "Stale vacancies"]);

        Assert.Equal("Some information could not be loaded: Open vacancies, Stale vacancies.", result);
    }

    [Fact]
    public void Compose_NoParts_ReturnsEmptyString()
    {
        Assert.Equal("", DashboardAnnouncements.Compose());
    }

    [Fact]
    public void Compose_SkipsNullEmptyAndWhitespaceParts_AndSingleSpaceJoinsTheRest()
    {
        var result = DashboardAnnouncements.Compose(
            "Recruitment dashboard finished loading.",
            null,
            "",
            "   ",
            "3 open vacancies.");

        Assert.Equal("Recruitment dashboard finished loading. 3 open vacancies.", result);
    }

    [Fact]
    public void Compose_SingleNonEmptyPart_ReturnedVerbatim()
    {
        Assert.Equal("Only this.", DashboardAnnouncements.Compose(null, "Only this.", null));
    }

    [Fact]
    public void Compose_DoesNotCollapseInternalSpacingOfIndividualParts()
    {
        // Only the join is single-spaced; each part is used as-is.
        Assert.Equal("a b c", DashboardAnnouncements.Compose("a b", "c"));
    }
}
