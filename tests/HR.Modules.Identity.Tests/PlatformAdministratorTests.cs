using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Domain-only (no DB) tests for PlatformAdministrator's P1 provisioning-workflow methods:
/// BeginProvisioning, MarkProvisioningFailed, CompleteProvisioning.
/// </summary>
public class PlatformAdministratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Defaults_ProvisioningStatus_To_Active()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);

        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, admin.ProvisioningStatus);
        Assert.Equal(1, admin.Version);
    }

    [Fact]
    public void BeginProvisioning_New_Account_Sets_PendingProvisioning()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        var correlationId = Guid.NewGuid();

        admin.BeginProvisioning(isNewIdentityProviderAccount: true, correlationId, Now);

        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingProvisioning, admin.ProvisioningStatus);
        Assert.True(admin.IsNewIdentityProviderAccount);
        Assert.Equal(correlationId, admin.ProvisioningCorrelationId);
        Assert.Equal(Now, admin.ProvisioningStartedAt);
    }

    [Fact]
    public void BeginProvisioning_Existing_Account_Sets_PendingLinkVerification()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        var correlationId = Guid.NewGuid();

        admin.BeginProvisioning(isNewIdentityProviderAccount: false, correlationId, Now);

        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingLinkVerification, admin.ProvisioningStatus);
        Assert.False(admin.IsNewIdentityProviderAccount);
        Assert.Equal(correlationId, admin.ProvisioningCorrelationId);
    }

    [Fact]
    public void MarkProvisioningFailed_Sets_Failed_Status_And_Reason()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), Now);

        admin.MarkProvisioningFailed("simulated_failure", Now);

        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, admin.ProvisioningStatus);
        Assert.Equal("simulated_failure", admin.ProvisioningFailureReason);
    }

    [Fact]
    public void CompleteProvisioning_From_PendingProvisioning_Succeeds_And_Links_SupabaseAuthUserId()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), Now);
        var supabaseUserId = Guid.NewGuid();

        var result = admin.CompleteProvisioning(supabaseUserId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, admin.ProvisioningStatus);
        Assert.Equal(supabaseUserId, admin.SupabaseAuthUserId);
        Assert.Equal(Now, admin.ProvisioningCompletedAt);
        Assert.Null(admin.ProvisioningFailureReason);
    }

    [Fact]
    public void CompleteProvisioning_From_PendingLinkVerification_Succeeds()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount: false, Guid.NewGuid(), Now);
        var supabaseUserId = Guid.NewGuid();

        var result = admin.CompleteProvisioning(supabaseUserId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, admin.ProvisioningStatus);
        Assert.Equal(supabaseUserId, admin.SupabaseAuthUserId);
    }

    [Fact]
    public void CompleteProvisioning_Clears_A_Previous_Failure_Reason()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), Now);
        admin.MarkProvisioningFailed("simulated_failure", Now);

        var result = admin.CompleteProvisioning(Guid.NewGuid(), Now);

        Assert.True(result.IsSuccess);
        Assert.Null(admin.ProvisioningFailureReason);
    }

    [Fact]
    public void CompleteProvisioning_Fails_With_Conflict_When_Already_Active()
    {
        // Create() defaults to Active with no BeginProvisioning call — the legacy/bootstrap-seeded path.
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);

        var result = admin.CompleteProvisioning(Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("already been activated", result.Error.Message);
    }

    [Fact]
    public void CompleteProvisioning_Fails_With_Conflict_When_Disabled()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), Now);
        admin.Disable(Now, actorUserId: null);

        var result = admin.CompleteProvisioning(Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("cancelled", result.Error.Message);
    }

    [Fact]
    public void IncrementVersion_Increases_Version_By_One()
    {
        var admin = PlatformAdministrator.Create("someone@test.com", PlatformAdministratorRole.SupportStaff, Now);

        admin.IncrementVersion();

        Assert.Equal(2, admin.Version);
    }
}
