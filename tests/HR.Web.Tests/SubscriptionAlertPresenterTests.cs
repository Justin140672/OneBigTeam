using HR.Modules.Companies.Contracts;
using HR.Web.Services;

namespace HR.Web.Tests;

public class SubscriptionAlertPresenterTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active, 0)]
    [InlineData(SubscriptionStatus.Trial, 4)]
    [InlineData(SubscriptionStatus.Trial, 30)]
    [InlineData(SubscriptionStatus.PastDue, 0)]
    [InlineData(SubscriptionStatus.Canceled, 0)]
    public void NoAlert_ForActiveAndHealthyTrial(SubscriptionStatus status, int days)
    {
        var model = SubscriptionAlertPresenter.Present(status, isReadOnly: false, days, canManageBilling: true);

        Assert.False(model.IsVisible);
        Assert.Equal(SubscriptionAlertVariant.None, model.Variant);
    }

    [Theory]
    [InlineData(3, "Your free trial ends in 3 days.")]
    [InlineData(2, "Your free trial ends in 2 days.")]
    [InlineData(1, "Your free trial ends in 1 day.")]
    [InlineData(0, "Your free trial ends today.")]
    public void TrialEndingSoon_ShowsWarningWithRemainingTimeAndAction(int days, string expectedText)
    {
        var model = SubscriptionAlertPresenter.Present(SubscriptionStatus.Trial, false, days, canManageBilling: true);

        Assert.Equal(SubscriptionAlertVariant.TrialEnding, model.Variant);
        Assert.Equal("trial-ending", model.VariantKey);
        Assert.Equal("Your free trial ends soon", model.Heading);
        Assert.Equal(expectedText, model.Text);
        Assert.Equal("Start subscription", model.ActionLabel);
        Assert.Equal("/subscription", model.ActionHref);
        Assert.Null(model.Guidance);
        Assert.False(model.IsUrgent);
    }

    [Fact]
    public void TrialExpired_ShowsExpiredAlertWithStartSubscription()
    {
        var model = SubscriptionAlertPresenter.Present(SubscriptionStatus.TrialExpired, true, 0, canManageBilling: true);

        Assert.Equal(SubscriptionAlertVariant.Expired, model.Variant);
        Assert.Equal("expired", model.VariantKey);
        Assert.Equal("Your free trial has ended", model.Heading);
        Assert.Equal(
            "Your account is now read-only. You can view existing information, but you cannot add or edit data until a subscription starts.",
            model.Text);
        Assert.Equal("Start subscription", model.ActionLabel);
        Assert.Equal("/subscription", model.ActionHref);
        Assert.True(model.IsUrgent);
    }

    [Fact]
    public void Paused_ShowsPausedAlertWithManageBilling()
    {
        var model = SubscriptionAlertPresenter.Present(SubscriptionStatus.Paused, true, 0, canManageBilling: true);

        Assert.Equal(SubscriptionAlertVariant.Paused, model.Variant);
        Assert.Equal("paused", model.VariantKey);
        Assert.Equal("Your subscription is paused", model.Heading);
        Assert.Equal(
            "Your account is now read-only. You can view existing information, but you cannot add or edit data until billing is restored.",
            model.Text);
        Assert.Equal("Manage billing", model.ActionLabel);
        Assert.Equal("/subscription", model.ActionHref);
        Assert.True(model.IsUrgent);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Canceled)]
    public void OtherReadOnlyStates_ShowGenericReadOnlyAlert(SubscriptionStatus status)
    {
        var model = SubscriptionAlertPresenter.Present(status, isReadOnly: true, 1, canManageBilling: true);

        Assert.Equal(SubscriptionAlertVariant.ReadOnly, model.Variant);
        Assert.Equal("read-only", model.VariantKey);
        Assert.Equal("Your account is read-only", model.Heading);
        Assert.True(model.IsUrgent);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial, false, 2, SubscriptionAlertVariant.TrialEnding)]
    [InlineData(SubscriptionStatus.TrialExpired, true, 0, SubscriptionAlertVariant.Expired)]
    [InlineData(SubscriptionStatus.Paused, true, 0, SubscriptionAlertVariant.Paused)]
    [InlineData(SubscriptionStatus.Active, true, 0, SubscriptionAlertVariant.ReadOnly)]
    public void WithoutBillingPermission_ShowsExplanationAndGuidanceButNoAction(
        SubscriptionStatus status, bool isReadOnly, int days, SubscriptionAlertVariant expected)
    {
        var model = SubscriptionAlertPresenter.Present(status, isReadOnly, days, canManageBilling: false);

        Assert.Equal(expected, model.Variant);
        Assert.NotEmpty(model.Heading);
        Assert.NotEmpty(model.Text);
        Assert.Null(model.ActionLabel);
        Assert.Null(model.ActionHref);
        Assert.Equal("Contact your Company Administrator to restore access.", model.Guidance);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial, false, 2)]
    [InlineData(SubscriptionStatus.TrialExpired, true, 0)]
    [InlineData(SubscriptionStatus.Paused, true, 0)]
    [InlineData(SubscriptionStatus.Active, true, 0)]
    public void WithBillingPermission_ShowsActionAndNoGuidance(SubscriptionStatus status, bool isReadOnly, int days)
    {
        var model = SubscriptionAlertPresenter.Present(status, isReadOnly, days, canManageBilling: true);

        Assert.NotNull(model.ActionLabel);
        Assert.Equal("/subscription", model.ActionHref);
        Assert.Null(model.Guidance);
    }

    [Fact]
    public void NoAlert_HasNoActionOrGuidanceEvenWithoutPermission()
    {
        var model = SubscriptionAlertPresenter.Present(SubscriptionStatus.Active, false, 0, canManageBilling: false);

        Assert.False(model.IsVisible);
        Assert.Null(model.ActionHref);
        Assert.Null(model.Guidance);
    }

    [Fact]
    public void VariantsUseDistinctIconsAndHeadings()
    {
        var models = new[]
        {
            SubscriptionAlertPresenter.Present(SubscriptionStatus.Trial, false, 1, true),
            SubscriptionAlertPresenter.Present(SubscriptionStatus.TrialExpired, true, 0, true),
            SubscriptionAlertPresenter.Present(SubscriptionStatus.Paused, true, 0, true),
            SubscriptionAlertPresenter.Present(SubscriptionStatus.Active, true, 0, true),
        };

        Assert.Equal(4, models.Select(m => m.IconCss).Distinct().Count());
        Assert.Equal(4, models.Select(m => m.Heading).Distinct().Count());
    }
}
