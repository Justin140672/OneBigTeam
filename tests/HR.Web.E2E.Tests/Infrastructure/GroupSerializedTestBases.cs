namespace HR.Web.E2E.Tests.Infrastructure;

// E2E test classes run fully in parallel (xunit.runner.json: parallelizeTestCollections = true,
// maxParallelThreads = 15). The only serialisation left is HR Settings:
//
// - HrSettingsSerialTestBase: whole classes that read/write the single shared CompanySettings row
//   for the Acme tenant (HrSettingsPageTests, DataImportWizardTests, ProfileOverviewTabTests).
// - HrSettingsSerialTestBase.GateInstance, acquired directly around just the mutating section of
//   individual methods that switch employee-numbering mode (CreateEmployeeTests' Manual-mode
//   methods, SharedCompanyDocumentAcknowledgementSettingsTests' mutating methods).
//
// Every other former serial group (vacancy/stage pipeline, leave notifications, documents and
// requests, tenant/misc, report favourites, position role defaults, shared probation review, real
// Supabase auth) was deliberately un-gated so those classes run concurrently. Any failures caused by
// genuine shared-state races between them are to be diagnosed and fixed individually.

/// <summary>
/// Base for a test class whose tests must run one-at-a-time relative to other classes gated on the
/// same static <see cref="HrSettingsSerialTestBase.GateInstance"/>, while remaining free to run
/// concurrently with every other class. Wraps E2ETestBase's per-test InitializeAsync/DisposeAsync
/// with acquire/release of the gate. xUnit creates a fresh test-class instance per [Fact]/[Theory]
/// method, so gating InitializeAsync/DisposeAsync serializes one test at a time.
/// </summary>
public abstract class HrSettingsSerialTestBase(HrSettingsSerialFixture fixture)
    : E2ETestBase(fixture), IClassFixture<HrSettingsSerialFixture>
{
    /// <summary>
    /// Shared static gate. Public so classes that can't derive from this base (different fixture)
    /// can still serialize their mutating methods against it directly.
    /// </summary>
    public static readonly SemaphoreSlim GateInstance = new(1, 1);

    public override async Task InitializeAsync()
    {
        await GateInstance.WaitAsync();
        try
        {
            await base.InitializeAsync();
        }
        catch
        {
            GateInstance.Release();
            throw;
        }
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            GateInstance.Release();
        }
    }
}
