namespace HR.Web.Services;

public enum SubscriptionResolution
{
    Unresolved,
    Resolved,
    Failed,
}

public enum MainLayoutMode
{
    Loading,
    Shell,
    CompleteProfile,
}

public static class MainLayoutModeResolver
{
    public static MainLayoutMode Resolve(
        bool isLoaded, SubscriptionResolution subscription, bool isReadOnly, bool requiresInitialSetup)
    {
        if (!isLoaded || subscription == SubscriptionResolution.Unresolved)
            return MainLayoutMode.Loading;

        if (subscription == SubscriptionResolution.Failed || isReadOnly)
            return MainLayoutMode.Shell;

        return requiresInitialSetup ? MainLayoutMode.CompleteProfile : MainLayoutMode.Shell;
    }
}
