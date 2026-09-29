using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class MarketingContentManagementTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

    [Fact]
    public async Task Feature_CreateEditPublishReorder_HappyPath()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var marketing = new MarketingContentPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await marketing.GoToAsync();

        Assert.False(await marketing.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed platform admin to see the marketing content editor, not the error banner");
        Assert.True(await marketing.IsFeaturesTableVisibleAsync(), "Expected the Features table to render");
        Assert.True(await marketing.HasFeatureAsync("Employee Management"),
            "Expected the seeded 'Employee Management' feature row to be present");

        var stamp = Stamp();
        var slug = $"e2e-{stamp}";
        var title = $"E2E Feature {stamp}";

        await marketing.ClickAddFeatureAsync();
        await marketing.FillFeatureFormAsync(
            slug: slug,
            title: title,
            iconName: "users",
            summary: "Created by MarketingContentManagementTests E2E.",
            deliveryStatus: "Available");
        await marketing.SaveFeatureAsync();

        Assert.True(await marketing.IsSuccessBannerVisibleAsync(),
            $"Expected the 'Saved.' banner after creating the feature. Error: {await SafeErrorAsync(marketing)}");
        Assert.True(await marketing.HasFeatureAsync(title), "Expected the newly-created feature row to appear");
        Assert.Equal("No", (await marketing.GetFeaturePublishedTextAsync(title))?.Trim());

        var updatedTitle = $"{title} (edited)";
        await marketing.ClickEditFeatureAsync(title);
        await marketing.FillFeatureFormAsync(title: updatedTitle);
        await marketing.SaveFeatureAsync();

        Assert.True(await marketing.IsSuccessBannerVisibleAsync(),
            $"Expected the 'Saved.' banner after editing the feature. Error: {await SafeErrorAsync(marketing)}");
        Assert.True(await marketing.HasFeatureAsync(updatedTitle),
            "Expected the edited feature title to show in the Features table");

        await marketing.ToggleFeaturePublicationAsync(updatedTitle);
        Assert.Equal("Yes", (await marketing.GetFeaturePublishedTextAsync(updatedTitle))?.Trim());

        await marketing.ToggleFeaturePublicationAsync(updatedTitle);
        Assert.Equal("No", (await marketing.GetFeaturePublishedTextAsync(updatedTitle))?.Trim());

        await marketing.ToggleFeaturePublicationAsync(updatedTitle);

        var before = await marketing.GetFeatureTitlesInOrderAsync();
        var startIndex = before.FindIndex(t => t == updatedTitle);
        Assert.True(startIndex > 0, "Expected the created feature to be below the first row before reordering");

        await marketing.MoveFeatureUpAsync(updatedTitle);

        var after = await marketing.GetFeatureTitlesInOrderAsync();
        var newIndex = after.FindIndex(t => t == updatedTitle);
        Assert.Equal(startIndex - 1, newIndex);
    }

    [Fact]
    public async Task Roadmap_CreateAndTogglePublication_HappyPath()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var marketing = new MarketingContentPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await marketing.GoToAsync();

        var stamp = Stamp();
        var title = $"E2E Roadmap {stamp}";

        await marketing.ClickAddRoadmapItemAsync();
        await marketing.FillRoadmapFormAsync(
            title: title,
            description: "Created by MarketingContentManagementTests E2E.",
            iconName: "chart-line",
            deliveryStatus: "Planned");
        await marketing.SaveRoadmapItemAsync();

        Assert.True(await marketing.IsSuccessBannerVisibleAsync(),
            $"Expected the 'Saved.' banner after creating the roadmap item. Error: {await SafeErrorAsync(marketing)}");
        Assert.True(await marketing.HasRoadmapItemAsync(title), "Expected the new roadmap item row to appear");

        var initialPublished = (await marketing.GetRoadmapPublishedTextAsync(title))?.Trim();
        await marketing.ToggleRoadmapPublicationAsync(title);
        var toggledPublished = (await marketing.GetRoadmapPublishedTextAsync(title))?.Trim();
        Assert.NotEqual(initialPublished, toggledPublished);

        await marketing.ToggleRoadmapPublicationAsync(title);
        Assert.Equal(initialPublished, (await marketing.GetRoadmapPublishedTextAsync(title))?.Trim());
    }

    [Fact]
    public async Task AnonymousAccess_ToMarketingContent_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/marketing-content");
        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }

    private static async Task<string?> SafeErrorAsync(MarketingContentPage marketing)
    {
        try
        {
            return await marketing.GetErrorListTextAsync();
        }
        catch
        {
            return null;
        }
    }
}
