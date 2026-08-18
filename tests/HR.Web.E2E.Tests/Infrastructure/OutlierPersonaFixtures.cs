namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Additional single-fixed-persona fixtures (same shape/benefits as the 4 canonical role
/// fixtures in RolePersonaFixtureBase — cached storageState login via PersonaLoginCache, real
/// per-class xUnit parallelism, no named [Collection]) for outlier personas used by test classes
/// that log in as ONE fixed persona for their whole lifetime but that persona isn't one of the 4
/// canonical roles. These personas don't need CrossUser's sequential execution or fresh-login-
/// per-test behavior — PersonaLoginCache/LoginPage.LoginAsync already cache login state for any
/// persona, not just the 4 canonical ones, so there is no cost benefit to routing these through
/// CrossUserFixture, and no correctness reason to serialize them against unrelated classes.
/// </summary>

/// <summary>Sarah Chen — used by task-view/notification test classes that stay on one persona throughout.</summary>
public sealed class SarahChenPersonaFixture() : RolePersonaFixtureBase("sarah.chen@acme.example");

/// <summary>Priya Shah — CompanyAdministrator-only persona, and the allow-listed platform admin used by several Admin Portal test classes.</summary>
public sealed class PriyaShahPersonaFixture() : RolePersonaFixtureBase("priya.shah@acme.example");
