namespace HR.Web.E2E.Tests.Infrastructure;

// The former single "CrossUser" xUnit collection (and the later "HrSettingsSerial" one) used
// xUnit's [CollectionDefinition(..., DisableParallelization = true)] mechanism to keep a group of
// test classes from racing each other on shared seeded state. A real timed run proved that xUnit
// v2 does NOT let multiple differently-named DisableParallelization collections run concurrently
// WITH EACH OTHER — every collection marked DisableParallelization = true is pulled into one
// single global non-parallel execution queue and run strictly one class at a time, regardless of
// how many separately-named collections exist. That meant all ~28 files below were serialized
// against every other file below, even across logically unrelated groups, adding ~25-30 minutes
// of pure sequential wall-clock time with zero possible parallelism gain from splitting further.
//
// FIX: these test classes are no longer xUnit collections at all. Each is now an ordinary
// parallel-eligible class (own default xUnit collection, IClassFixture-wired exactly like the
// ~139 role-fixed classes), so xUnit schedules them for real concurrency governed by
// maxParallelThreads in xunit.runner.json. "Don't race with the other files in the same logical
// group" is now enforced explicitly via a per-group static SemaphoreSlim(1, 1) that every test
// acquires in InitializeAsync and releases in DisposeAsync (wrapping the actual
// E2ETestBase.InitializeAsync/DisposeAsync login/teardown behavior, not replacing it). Because
// xUnit creates a fresh instance of the test class — and therefore a fresh IAsyncLifetime
// InitializeAsync/DisposeAsync pair — per [Fact]/[Theory] method (confirmed by the existing
// ~139 role-fixed classes already relying on this to get a fresh IPage/IBrowserContext per test
// method), gating InitializeAsync/DisposeAsync here naturally serializes one test at a time WITHIN
// a group while leaving different groups free to run fully concurrently with each other.
//
// Five independent semaphores below — one per logical group — so the 5 groups can genuinely
// overlap in real time. See each group's own tests for the shared-state rationale that requires
// internal serialization (unchanged from the previous collection-based design):
//
// - CrossUserVacancy: Vacancy / Position Profile / recruitment-pipeline tests. Every mutated
//   entity is created fresh with a unique GUID-suffixed name in each test.
// - CrossUserLeaveNotifications: Tom Williams submitting leave / James Okafor reviewing it via his
//   aggregate task list and notification bell. NotificationMarkAllReadTests mutates James's entire
//   notification set, which collides with the other three files in this group.
// - CrossUserDocumentsAndRequests: Tom/Laura shared-company-document and personal-details-change-
//   request flows.
// - CrossUserTenantAndMisc: Beta Corp cross-tenant isolation tests plus a few standalone files that
//   only touch their own dedicated seeded entities.
// - HrSettingsSerial: tests that read/write the single shared CompanySettings row for the Acme
//   tenant.
// - HrFavouritesSerial: HrDashboardTests and ReportCatalogTests — both toggle Laura Bennett's
//   server-persisted report favourites (ReportingService's Add/RemoveReportFavouriteAsync) and
//   restore them to empty afterward, but under real concurrency one file's transient favourite
//   (mid-test, before its own cleanup) is visible to the other file's "starts with zero
//   favourites" / "only this one favourite" assertions, since both read/write the exact same
//   persona's favourites. Same convention as HrSettingsSerial (single shared row, not a per-test
//   uniquely-named entity).
//
// - SupabaseAuthSerial: the handful of tests that make a REAL network call to Supabase Auth
//   (signup, or a login not served by PersonaLoginCache's storageState reuse) rather than the
//   dev-persona shortcut every other test relies on. Unlike the groups above, this isn't about
//   shared seeded-data races — every mutated record here is already uniquely named per test. The
//   problem is Supabase-side: under full-suite concurrent execution, several real signup/login
//   calls firing at once hit Supabase's own latency/rate-limiting, which surfaced as ordinary
//   Playwright timeouts (SignupPageRedesignTests / SignupToCheckYourEmailJourneyTests) and as
//   outright "Real Supabase login ... failed after 3 attempts" exceptions (SelfServiceDocumentTests,
//   AssetReturnTaskTests — both log in as a one-off persona outside the cached role fixtures).
//   Serializing this group trades a little wall-clock time for not hammering Supabase with bursts
//   of concurrent real auth calls; it does not touch the app's dev-persona fast path used by the
//   ~139 role-fixed classes, so it doesn't reintroduce the old whole-suite serialization cost.

/// <summary>
/// Base for a test class whose tests must run one-at-a-time relative to other classes in the same
/// named group, while remaining free to run concurrently with classes in other groups. Wraps
/// E2ETestBase's per-test InitializeAsync/DisposeAsync with acquire/release of <see cref="Gate"/>.
/// </summary>
public abstract class GroupSerializedE2ETestBase<TFixture>(TFixture fixture)
    : E2ETestBase(fixture), IClassFixture<TFixture>
    where TFixture : class, IPersonaFixture
{
    /// <summary>The per-group serialization gate. Must be a single shared static instance per group.</summary>
    protected abstract SemaphoreSlim Gate { get; }

    public override async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>Vacancy / Position Profile / recruitment-pipeline tests — see rationale above.</summary>
public abstract class CrossUserVacancyTestBase(CrossUserFixture fixture)
    : GroupSerializedE2ETestBase<CrossUserFixture>(fixture)
{
    // Exposed publicly (not just via the protected Gate override below) so a class that can't
    // join this group at the type level — e.g. RecruitmentStageManagementTests, which uses
    // RecruiterPersonaFixture rather than CrossUserFixture — can still serialize its own
    // IAsyncLifetime against the same instance directly. See RecruitmentStageManagementTests'
    // own remarks for the specific race this covers: it mutates the single shared, ordered list
    // of Acme recruitment pipeline stages (create/reorder/deactivate), which every test in this
    // group reads by stage name/position (e.g. expecting "Offer" to still be the offer stage).
    public static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>Tom/James leave-approval and notification-bell tests — see rationale above.</summary>
public abstract class CrossUserLeaveNotificationsTestBase(CrossUserFixture fixture)
    : GroupSerializedE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>Tom/Laura shared-document and personal-details-change-request tests — see rationale above.</summary>
public abstract class CrossUserDocumentsAndRequestsTestBase(CrossUserFixture fixture)
    : GroupSerializedE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>Beta Corp cross-tenant isolation tests plus unrelated standalone files — see rationale above.</summary>
public abstract class CrossUserTenantAndMiscTestBase(CrossUserFixture fixture)
    : GroupSerializedE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>Tests that mutate the single shared CompanySettings row for the Acme tenant — see rationale above.</summary>
public abstract class HrSettingsSerialTestBase(HrSettingsSerialFixture fixture)
    : GroupSerializedE2ETestBase<HrSettingsSerialFixture>(fixture)
{
    private static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>HrDashboardTests / ReportCatalogTests — both toggle Laura Bennett's shared, server-persisted report favourites — see rationale above.</summary>
public abstract class HrFavouritesSerialTestBase(HrAdminPersonaFixture fixture)
    : GroupSerializedE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly SemaphoreSlim GateInstance = new(1, 1);
    protected override SemaphoreSlim Gate => GateInstance;
}

/// <summary>
/// Single gate shared by both SupabaseAuthSerial bases below, so that a blank-persona signup test
/// and a one-off-login test never make real Supabase Auth calls concurrently with each other
/// either — see rationale above. Two base classes exist only because the affected test classes use
/// two different fixture types (ParallelBlankPersonaFixture vs. the role-fixed EmployeePersonaFixture),
/// not because the tests need separate serialization domains.
/// </summary>
public static class SupabaseAuthGate
{
    public static readonly SemaphoreSlim Instance = new(1, 1);
}

/// <summary>Real-Supabase-call tests on a blank (no fixed persona) context — signup flows — see rationale above.</summary>
public abstract class SupabaseAuthSerialBlankTestBase(ParallelBlankPersonaFixture fixture)
    : GroupSerializedE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    protected override SemaphoreSlim Gate => SupabaseAuthGate.Instance;
}

/// <summary>Real-Supabase-call tests that log in as the Employee persona outside PersonaLoginCache's reuse path — see rationale above.</summary>
public abstract class SupabaseAuthSerialEmployeeTestBase(EmployeePersonaFixture fixture)
    : GroupSerializedE2ETestBase<EmployeePersonaFixture>(fixture)
{
    protected override SemaphoreSlim Gate => SupabaseAuthGate.Instance;
}

/// <summary>
/// A narrow, method-level (not class-level) serialization gate for the single seeded Sophie
/// Laurent probation review shared between two files in two different class-level groups:
/// ProbationReviewFlowTests.CompletingReviewTask_IsReflectedOnProbationTab (group
/// CrossUserTenantAndMiscTestBase) completes her seeded ManagerCheckIn review, and
/// HrDashboardTests.UpcomingProbationReviewsWidget_ShowsCarlosRivera (group
/// HrFavouritesSerialTestBase) reads the "upcoming probation reviews" widget, which is capped to
/// a small number of rows sorted by due date — while Sophie's review is still pending it can sort
/// ahead of and evict Carlos Rivera's seeded pending review from that capped list. Previously, the
/// single whole-suite CrossUser collection accidentally serialized every file against every other
/// file (see rationale above), so this race never manifested; splitting into independent
/// per-group semaphores restored real concurrency between those two groups and exposed it. Adding
/// a full class to either group isn't appropriate (their fixtures don't match and neither file's
/// other tests need serializing against the other group), so this is acquired directly, only
/// around the specific mutating/reading statements in those two test methods, instead of via
/// class-level inheritance like the groups above.
/// </summary>
public static class SharedProbationGate
{
    public static readonly SemaphoreSlim Instance = new(1, 1);
}
