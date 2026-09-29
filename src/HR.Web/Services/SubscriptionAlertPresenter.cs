using HR.Modules.Companies.Contracts;

namespace HR.Web.Services;

public enum SubscriptionAlertVariant
{
    None,
    TrialEnding,
    Expired,
    Paused,
    ReadOnly,
}

public sealed record SubscriptionAlertModel(
    SubscriptionAlertVariant Variant,
    string Heading,
    string Text,
    string? ActionLabel,
    string? ActionHref,
    string? Guidance)
{
    public static SubscriptionAlertModel None { get; } = new(SubscriptionAlertVariant.None, "", "", null, null, null);

    public bool IsVisible => Variant != SubscriptionAlertVariant.None;

    public bool IsUrgent => Variant is SubscriptionAlertVariant.Expired
        or SubscriptionAlertVariant.Paused
        or SubscriptionAlertVariant.ReadOnly;

    public string VariantKey => Variant switch
    {
        SubscriptionAlertVariant.TrialEnding => "trial-ending",
        SubscriptionAlertVariant.Expired => "expired",
        SubscriptionAlertVariant.Paused => "paused",
        SubscriptionAlertVariant.ReadOnly => "read-only",
        _ => "none",
    };

    public string IconCss => Variant switch
    {
        SubscriptionAlertVariant.TrialEnding => "fa-solid fa-hourglass-half",
        SubscriptionAlertVariant.Expired => "fa-solid fa-lock",
        SubscriptionAlertVariant.Paused => "fa-solid fa-circle-pause",
        SubscriptionAlertVariant.ReadOnly => "fa-solid fa-eye",
        _ => "",
    };
}

public static class SubscriptionAlertPresenter
{
    public const int TrialEndingThresholdDays = 3;
    public const string SubscriptionHref = "/subscription";
    public const string AdminGuidance = "Contact your Company Administrator to restore access.";

    public static SubscriptionAlertModel Present(
        SubscriptionStatus status, bool isReadOnly, int trialDaysRemaining, bool canManageBilling)
    {
        var (variant, heading, text, actionLabel) = Resolve(status, isReadOnly, trialDaysRemaining);

        if (variant == SubscriptionAlertVariant.None)
            return SubscriptionAlertModel.None;

        return canManageBilling
            ? new SubscriptionAlertModel(variant, heading, text, actionLabel, SubscriptionHref, null)
            : new SubscriptionAlertModel(variant, heading, text, null, null, AdminGuidance);
    }

    public static SubscriptionAlertModel Present(AppSession session) =>
        Present(session.SubscriptionStatus, session.IsReadOnly, session.TrialDaysRemaining, session.CanManageCompany);

    private static (SubscriptionAlertVariant Variant, string Heading, string Text, string ActionLabel) Resolve(
        SubscriptionStatus status, bool isReadOnly, int trialDaysRemaining)
    {
        if (status == SubscriptionStatus.TrialExpired)
            return (SubscriptionAlertVariant.Expired,
                "Your free trial has ended",
                "Your account is now read-only. You can view existing information, but you cannot add or edit data until a subscription starts.",
                "Start subscription");

        if (status == SubscriptionStatus.Paused)
            return (SubscriptionAlertVariant.Paused,
                "Your subscription is paused",
                "Your account is now read-only. You can view existing information, but you cannot add or edit data until billing is restored.",
                "Manage billing");

        if (isReadOnly)
            return (SubscriptionAlertVariant.ReadOnly,
                "Your account is read-only",
                "You can view existing information, but you cannot add or edit data until access is restored.",
                "Manage billing");

        if (status == SubscriptionStatus.Trial && trialDaysRemaining <= TrialEndingThresholdDays)
            return (SubscriptionAlertVariant.TrialEnding,
                "Your free trial ends soon",
                TrialRemainingText(trialDaysRemaining),
                "Start subscription");

        return (SubscriptionAlertVariant.None, "", "", "");
    }

    private static string TrialRemainingText(int days) => days == 0
        ? "Your free trial ends today."
        : $"Your free trial ends in {days} day{(days == 1 ? "" : "s")}.";
}
