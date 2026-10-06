using HR.Modules.Companies.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Undoes the externally visible effects of an incomplete signup operation: deactivates the company
/// shell and deletes the Supabase account, but only when that account is proven to belong to this
/// operation (recorded id, or provisioning-operation metadata match). The caller must hold the
/// operation's lease so a slow original owner is fenced out by the version check.
/// </summary>
internal sealed class SignUpOperationCompensator(
    IdentityDbContext dbContext,
    ICompanyProvisioner companyProvisioner,
    ISupabaseAuthGateway supabaseAuthGateway,
    IAuditEventPublisher auditEventPublisher,
    IClock clock,
    ILogger<SignUpOperationCompensator> logger)
{
    public async Task<bool> CompensateAsync(
        SignUpOperation operation,
        string failureCode,
        string failureMessage,
        bool releaseKey,
        CancellationToken cancellationToken)
    {
        await companyProvisioner.DeactivateCompanyAsync(operation.CompanyId, cancellationToken);

        var supabaseUserId = operation.SupabaseAuthUserId ?? await ResolveOwnedSupabaseUserAsync(operation, cancellationToken);
        if (supabaseUserId is { } id)
        {
            await supabaseAuthGateway.DeleteUserAsync(id, cancellationToken);
        }

        var now = clock.UtcNowOffset();
        var expectedVersion = operation.Version;
        operation.Fail(failureCode, failureMessage, releaseKey, now);

        var save = await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion, "The signup operation changed while it was being compensated.", cancellationToken);
        if (save.IsFailure)
        {
            logger.LogWarning("Signup operation {OperationId} changed during compensation; leaving it for reconciliation.", operation.Id);
            return false;
        }

        logger.LogInformation(
            "Signup operation {OperationId} compensated at stage transition to {Stage}: company {CompanyId} deactivated, identity removed: {IdentityRemoved}.",
            operation.Id, operation.Stage, operation.CompanyId, supabaseUserId is not null);

        await auditEventPublisher.PublishAsync(
            new RegistrationCreatedAuditEvent(operation.CompanyId, AdminUserId: null, now, Succeeded: false, failureCode),
            CancellationToken.None);

        return true;
    }

    private async Task<Guid?> ResolveOwnedSupabaseUserAsync(SignUpOperation operation, CancellationToken cancellationToken)
    {
        var existing = await supabaseAuthGateway.GetUserMetadataByEmailAsync(operation.AdminEmail, cancellationToken);

        return existing is { } found
            && found.Metadata.TryGetValue(SignUpOperation.ProvisioningMetadataKey, out var correlation)
            && correlation == operation.Id.ToString()
                ? found.UserId
                : null;
    }
}
