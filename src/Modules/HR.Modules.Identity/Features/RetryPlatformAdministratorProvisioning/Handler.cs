using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Features.RetryPlatformAdministratorProvisioning;

/// <summary>
/// P1: re-attempts the identity-provider side of provisioning for a Failed (or still-Pending, e.g.
/// the original email simply never arrived) platform-administrator row, without ever creating a
/// duplicate local record or a duplicate identity-provider account — reuses the row's own
/// persisted ProvisioningCorrelationId exactly as the original attempt would have.
/// </summary>
internal sealed class RetryPlatformAdministratorProvisioningHandler(
    IdentityDbContext db,
    CreatePlatformAdministratorHandler provisioningDelivery,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<RetryPlatformAdministratorProvisioningResponse>> HandleAsync(
        RetryPlatformAdministratorProvisioningRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<RetryPlatformAdministratorProvisioningResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<RetryPlatformAdministratorProvisioningResponse>(Error.NotFound("Platform administrator was not found."));

        if (administrator.ProvisioningStatus == PlatformAdministratorProvisioningStatus.Active)
            return Result.Failure<RetryPlatformAdministratorProvisioningResponse>(
                Error.Conflict("This administrator account has already been activated."));

        if (!administrator.IsEnabled)
            return Result.Failure<RetryPlatformAdministratorProvisioningResponse>(
                Error.Conflict("This administrator invitation has been cancelled. Re-enable the account before retrying."));

        if (administrator.ProvisioningCorrelationId is not { } correlationId)
            return Result.Failure<RetryPlatformAdministratorProvisioningResponse>(
                Error.Conflict("This administrator record predates the provisioning workflow and cannot be retried automatically."));

        var webBaseUrl =
            configuration["WebApp:BaseUrl"]?.TrimEnd('/') ??
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/platform-admin/activate";

        var now = clock.UtcNowOffset();

        await provisioningDelivery.AttemptProvisioningDeliveryAsync(
            administrator, administrator.IsNewIdentityProviderAccount, correlationId, redirectTo, currentUser.UserId, now, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorProvisioningRetriedAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new RetryPlatformAdministratorProvisioningResponse(administrator.Id, administrator.ProvisioningStatus));
    }
}
