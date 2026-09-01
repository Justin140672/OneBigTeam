namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Deterministic, pre-seeded Acme employees created by
/// <c>HR.Modules.Employees.EmployeesModule.SeedEmployeesAsync</c> (E2E test pool section, only
/// when the API host runs with <c>E2E_TESTING=true</c> — never in the integration-test DB or any
/// real environment). These exist so tests that need "an employee to act on" as arrange — not as
/// the thing under test — can reference a stable row instead of paying the full New Employee form
/// (4 combobox selections + 2 navigations) 15-25 times per class.
///
/// Every pool member: FirstName "E2E", Gender Male, Nationality British, DOB 1990-06-15,
/// StartDate 2026-03-01, EmploymentType "Permanent", Position Profile "QA Engineer"
/// (=> Engineering department + London Office), a starting Compensation record (£50,000) and an
/// "Employee joined" timeline entry dated 2026-03-01, plus a NotStarted onboarding plan with the
/// 3 default checklist tasks (Program.cs -> OnboardingModule.SeedE2eOnboardingPlansAsync) —
/// mirroring what CreateEmployeeHandler / EmployeeCreatedHandler produce for a UI-created employee.
///
/// The GUIDs / emails / last names / David-Park manager flags below are duplicated verbatim from
/// <c>EmployeesModule.E2eTestPool</c> (same cross-project hardcoded-constant pattern already used
/// for CompanyId / LeavePolicyId across module seed methods). Keep the two in sync.
/// GUID scheme: 3E2E0000-0000-0000-0000-0000000000NN (NN = two-digit index).
/// </summary>
public static class SeededE2eEmployees
{
    public static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Manager the ManagerDashboard pool members report to (David Park), assigned in the seed.</summary>
    public static readonly Guid ManagerDavidParkId = Guid.Parse("30000000-0000-0000-0000-000000000008");

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

    // ── Single read-only consumer ────────────────────────────────────────────
    public static readonly Pooled ProfileViewEditMode = P(1, "SeedProfileView");

    // ── Timeline tab (each test mutates its employee: notes / promotion) ─────
    public static readonly IReadOnlyList<Pooled> Timeline =
    [
        P(2, "SeedTimelineA"), P(3, "SeedTimelineB"), P(4, "SeedTimelineC"),
    ];

    // ── Lifecycle tab visibility (tests 2 & 3 start a leaving process) ───────
    public static readonly IReadOnlyList<Pooled> LifecycleTabVisibility =
    [
        P(5, "SeedLifecycleA"), P(6, "SeedLifecycleB"),
    ];

    // ── Employment tab notice-period override (mutates notice period; one per test) ──
    public static readonly IReadOnlyList<Pooled> NoticePeriodOverride =
    [
        P(7, "SeedNoticePeriodA"), P(8, "SeedNoticePeriodB"), P(9, "SeedNoticePeriodC"),
    ];

    // ── Employee list UI (search / display only) ─────────────────────────────
    public static readonly IReadOnlyList<Pooled> ListUi =
    [
        P(10, "SeedListUiA"), P(11, "SeedListUiB"), P(12, "SeedListUiC"),
    ];

    // ── Employee list bulk update (each test consumes a pair) ────────────────
    public static readonly IReadOnlyList<Pooled> ListBulkUpdate =
    [
        P(13, "SeedBulkA"), P(14, "SeedBulkB"), P(15, "SeedBulkC"), P(16, "SeedBulkD"),
        P(17, "SeedBulkE"), P(18, "SeedBulkF"), P(19, "SeedBulkG"), P(20, "SeedBulkH"),
    ];

    // ── Manager dashboard (pre-assigned to David Park in the seed) ───────────
    public static readonly IReadOnlyList<Pooled> ManagerDashboard =
    [
        P(21, "SeedMgrDashA"), P(22, "SeedMgrDashB"), P(23, "SeedMgrDashC"),
    ];

    // ── Login-as-employee consumers (Employee row seeded; login still via runtime EnsureEmployeeLoginAsync) ──
    public static readonly Pooled AssetAcknowledgement = P(24, "SeedAssetAck");
    public static readonly Pooled AssetReturn = P(25, "SeedAssetReturn");
    public static readonly Pooled SelfServiceDocument = P(26, "SeedSelfServiceDoc");

    // ── Leaving process (each test consumes one by starting a leaving process) ──
    public static readonly IReadOnlyList<Pooled> LeavingProcess =
    [
        P(27, "SeedLeavingA"), P(28, "SeedLeavingB"), P(29, "SeedLeavingC"), P(30, "SeedLeavingD"),
        P(31, "SeedLeavingE"), P(32, "SeedLeavingF"), P(33, "SeedLeavingG"), P(34, "SeedLeavingH"),
    ];

    // ── Offboarding tab (consume-one-per-test) ───────────────────────────────
    public static readonly IReadOnlyList<Pooled> OffboardingTab =
    [
        P(35, "SeedOffboardTabA"), P(36, "SeedOffboardTabB"),
        P(37, "SeedOffboardTabC"), P(38, "SeedOffboardTabD"),
    ];

    // ── Offboarding confirmation (consume-one-per-test) ──────────────────────
    public static readonly IReadOnlyList<Pooled> OffboardingConfirmation =
    [
        P(39, "SeedOffboardConfA"), P(40, "SeedOffboardConfB"),
        P(41, "SeedOffboardConfC"), P(42, "SeedOffboardConfD"),
    ];

    // ── Onboarding tab (each test consumes one — NotStarted plan + 3 default tasks) ──
    public static readonly IReadOnlyList<Pooled> OnboardingTab =
    [
        P(43, "SeedOnboardTabA"), P(44, "SeedOnboardTabB"), P(45, "SeedOnboardTabC"),
        P(46, "SeedOnboardTabD"), P(47, "SeedOnboardTabE"), P(48, "SeedOnboardTabF"),
    ];

    /// <summary>Every pool member, for callers that just need to enumerate them.</summary>
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
    }
}
