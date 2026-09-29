namespace HR.Web.E2E.Tests.Infrastructure;

public static class SeededE2eEmployees
{
    public static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static readonly Guid ManagerDavidParkId = Guid.Parse("30000000-0000-0000-0000-000000000008");

    public static readonly Guid DedicatedManagerNinaPatelId = Guid.Parse("30000000-0000-0000-0000-000000000017");
    public const string DedicatedManagerNinaPatelEmail = "nina.patel@acme.example";

    public sealed record Pooled(int Index, Guid EmployeeId, string LastName, string Email, string EmployeeNumber)
    {
        public string FullName => $"E2E {LastName}";
    }

    private static Pooled P(int nn, string lastName) => new(
        nn,
        Guid.Parse($"3E2E0000-0000-0000-0000-0000000000{nn:D2}"),
        lastName,
        $"e2e.seed{nn:D2}@acme.example",
        $"E2E-SEED-{nn:D2}");

    public static readonly Pooled ProfileViewEditMode = P(1, "SeedProfileView");

    public static readonly IReadOnlyList<Pooled> Timeline =
    [
        P(2, "SeedTimelineA"), P(3, "SeedTimelineB"), P(4, "SeedTimelineC"),
    ];

    public static readonly IReadOnlyList<Pooled> LifecycleTabVisibility =
    [
        P(5, "SeedLifecycleA"), P(6, "SeedLifecycleB"),
    ];

    public static readonly IReadOnlyList<Pooled> NoticePeriodOverride =
    [
        P(7, "SeedNoticePeriodA"), P(8, "SeedNoticePeriodB"), P(9, "SeedNoticePeriodC"),
    ];

    public static readonly IReadOnlyList<Pooled> ListUi =
    [
        P(10, "SeedListUiA"), P(11, "SeedListUiB"), P(12, "SeedListUiC"),
    ];

    public static readonly IReadOnlyList<Pooled> ListBulkUpdate =
    [
        P(13, "SeedBulkA"), P(14, "SeedBulkB"), P(15, "SeedBulkC"), P(16, "SeedBulkD"),
        P(17, "SeedBulkE"), P(18, "SeedBulkF"), P(19, "SeedBulkG"), P(20, "SeedBulkH"),
    ];

    public static readonly IReadOnlyList<Pooled> ManagerDashboard =
    [
        P(21, "SeedMgrDashA"), P(22, "SeedMgrDashB"), P(23, "SeedMgrDashC"),
    ];

    public static readonly Pooled AssetAcknowledgement = P(24, "SeedAssetAck");
    public static readonly Pooled AssetReturn = P(25, "SeedAssetReturn");
    public static readonly Pooled SelfServiceDocument = P(26, "SeedSelfServiceDoc");

    public static readonly IReadOnlyList<Pooled> LeavingProcess =
    [
        P(27, "SeedLeavingA"), P(28, "SeedLeavingB"), P(29, "SeedLeavingC"), P(30, "SeedLeavingD"),
        P(31, "SeedLeavingE"), P(32, "SeedLeavingF"), P(33, "SeedLeavingG"), P(34, "SeedLeavingH"),
    ];

    public static readonly IReadOnlyList<Pooled> OffboardingTab =
    [
        P(35, "SeedOffboardTabA"), P(36, "SeedOffboardTabB"),
        P(37, "SeedOffboardTabC"), P(38, "SeedOffboardTabD"),
    ];

    public static readonly IReadOnlyList<Pooled> OffboardingConfirmation =
    [
        P(39, "SeedOffboardConfA"), P(40, "SeedOffboardConfB"),
        P(41, "SeedOffboardConfC"), P(42, "SeedOffboardConfD"),
    ];

    public static readonly IReadOnlyList<Pooled> OnboardingTab =
    [
        P(43, "SeedOnboardTabA"), P(44, "SeedOnboardTabB"), P(45, "SeedOnboardTabC"),
        P(46, "SeedOnboardTabD"), P(47, "SeedOnboardTabE"), P(48, "SeedOnboardTabF"),
    ];

    // ── Optimistic-concurrency conflict UI (Ticket 2) ───────────────────────
    // ConcurrencyAdmin: HR-admin editor test — two admin browser tabs on the same employee.
    // ConcurrencySelf: self-service Contact Details test — needs a runtime Supabase login via
    // the dev ensure-employee-login endpoint (its Employee row is seeded, its login is not).
    public static readonly Pooled ConcurrencyAdmin = P(49, "SeedConcurrencyAdmin");
    public static readonly Pooled ConcurrencySelf  = P(50, "SeedConcurrencySelf");

    // ── Ticket 7: self-service Contact Details "saving" control tests ─────────
    // One dedicated login-less pool employee per held-save test (runtime Supabase login via the
    // dev ensure-employee-login endpoint). The HR.Web test-only save-control store keys on the
    // employee email, so each test controlling its own employee is fully isolated.
    public static readonly IReadOnlyList<Pooled> ContactSaveControl =
    [
        P(51, "SeedContactSaveA"), P(52, "SeedContactSaveB"),
        P(53, "SeedContactSaveC"), P(54, "SeedContactSaveD"),
    ];

    public static readonly IReadOnlyList<Pooled> BulkInvite =
    [
        P(55, "SeedInviteA"), P(56, "SeedInviteB"), P(57, "SeedInviteC"), P(58, "SeedInviteD"),
        P(59, "SeedInviteE"), P(60, "SeedInviteF"), P(61, "SeedInviteG"), P(62, "SeedInviteH"),
    ];

    public static readonly Pooled CompensationEdit = P(63, "SeedCompEdit");

    public static readonly Pooled QuickInvite = P(64, "SeedQuickInvite");

    public static IEnumerable<Pooled> All()
    {
        yield return ProfileViewEditMode;
        foreach (var p in Timeline) yield return p;
        foreach (var p in LifecycleTabVisibility) yield return p;
        foreach (var p in NoticePeriodOverride) yield return p;
        foreach (var p in ListUi) yield return p;
        foreach (var p in ListBulkUpdate) yield return p;
        foreach (var p in ManagerDashboard) yield return p;
        yield return AssetAcknowledgement;
        yield return AssetReturn;
        yield return SelfServiceDocument;
        foreach (var p in LeavingProcess) yield return p;
        foreach (var p in OffboardingTab) yield return p;
        foreach (var p in OffboardingConfirmation) yield return p;
        foreach (var p in OnboardingTab) yield return p;
        yield return ConcurrencyAdmin;
        yield return ConcurrencySelf;
        foreach (var p in ContactSaveControl) yield return p;
        foreach (var p in BulkInvite) yield return p;
        yield return CompensationEdit;
        yield return QuickInvite;
    }
}
