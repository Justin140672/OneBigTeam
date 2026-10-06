using HR.Modules.Companies.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Undoes the externally visible effects of an incomplete signup operation: deactivates the company
/// shell and deletes the Supabase account, but only when that account carries this operation's
/// correlation id. The caller must have just taken the operation's lease; the first thing done is a
/// fenced save that persists the compensating state, so a worker that lost the lease (or a normal
/// processing run) cannot advance or complete the operation afterwards, and a stale caller never
/// reaches a destructive step.
/// </summary>
internal sealed class SignUpOperationCompensator(
    IdentityDbContext dbContext,
    ICompanyProvisioner companyProvisioner,
    ISupabaseAuthGateway supabaseAuthGateway,
    IAuditEventPublisher auditEventPublisher,
    IClock clock,
    ILogger<SignUpOperationCompensator> logger)
{
    internal static readonly TimeSpan CompensationLease = TimeSpan.FromMinutes(3);

    public async Task<bool> CompensateAsync(
        SignUpOperation operation,
        string failureCode,
        string failureMessage,
        bool releaseKey,
        CancellationToken cancellationToken)
    {
        if (!operation.IsCompensating)
        {
            var begin = operation.Version;
            operation.BeginCompensation(failureCode, releaseKey, clock.UtcNowOffset(), CompensationLease);
            var started = await dbContext.SaveChangesWithConcurrencyAsync(
                operation, begin, "The signup operation changed before it could be compensated.", cancellationToken);
            if (started.IsFailure)
            {
                logger.LogWarning("Signup operation {OperationId} is owned by another worker; compensation skipped.", operation.Id);
                return false;
            }
        }
        else
        {
            failureCode = operation.CompensationCode ?? failureCode;
            releaseKey = operation.CompensationReleaseKey;
        }

        if (!await RenewAsync(operation, cancellationToken))
        {
            return false;
        }

        await companyProvisioner.DeactivateCompanyAsync(operation.CompanyId, cancellationToken);

        if (!await RenewAsync(operation, cancellationToken))
        {
            return false;
        }

        var identityRemoved = await DeleteOwnedSupabaseUserAsync(operation, cancellationToken);

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
            "Signup operation {OperationId} compensated: company {CompanyId} deactivated, identity removed: {IdentityRemoved}.",
            operation.Id, operation.CompanyId, identityRemoved);

        await auditEventPublisher.PublishAsync(
            new RegistrationCreatedAuditEvent(operation.CompanyId, AdminUserId: null, now, Succeeded: false, failureCode),
            CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Removes anything a worker that lost its lease may have created after the operation was
    /// compensated: the (idempotently re-deactivated) company and an owned Supabase account. Safe to
    /// repeat; never touches resources whose ownership cannot be proven.
    /// </summary>
    public async Task RemoveLateResourcesAsync(SignUpOperation operation, CancellationToken cancellationToken)
    {
        await companyProvisioner.DeactivateCompanyAsync(operation.CompanyId, cancellationToken);
        await DeleteOwnedSupabaseUserAsync(operation, cancellationToken);
    }

    private async Task<bool> RenewAsync(SignUpOperation operation, CancellationToken cancellationToken)
    {
        var expectedVersion = operation.Version;
        operation.RenewLease(clock.UtcNowOffset(), CompensationLease);
        var renewed = await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion, "The signup operation changed while it was being compensated.", cancellationToken);
        return renewed.IsSuccess;
    }

    private async Task<bool> DeleteOwnedSupabaseUserAsync(SignUpOperation operation, CancellationToken cancellationToken)
    {
        var existing = await supabaseAuthGateway.GetUserMetadataByEmailAsync(operation.AdminEmail, cancellationToken);

        var owned = existing is { } found
            && found.Metadata.TryGetValue(SignUpOperation.ProvisioningMetadataKey, out var correlation)
            && correlation == operation.Id.ToString()
            && (operation.SupabaseAuthUserId is null || operation.SupabaseAuthUserId == found.UserId);

        if (!owned)
        {
            return false;
        }

        await supabaseAuthGateway.DeleteUserAsync(existing!.Value.UserId, cancellationToken);
        return true;
    }
}
