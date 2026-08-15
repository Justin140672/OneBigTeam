using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

// DELIBERATE STUB (same conservative-stub precedent as "Login As Customer" elsewhere in this
// codebase): no Supabase Admin API MFA-factor-management method exists anywhere in
// ISupabaseAuthGateway/SupabaseAuthGateway today, and adding real Supabase MFA factor
// enumeration/deletion is out of scope for this pass.
//
// A real implementation would require a new ISupabaseAuthGateway method that calls Supabase's
// Admin API to list the user's MFA factors (GET /auth/v1/admin/users/{user_id}/factors) and delete
// each one (DELETE /auth/v1/admin/users/{user_id}/factors/{factor_id}). This handler only records
// that a reset was requested — it does not perform any actual MFA factor removal.
internal sealed class ResetPlatformAdministratorMfaHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ResetPlatformAdministratorMfaResponse>> HandleAsync(
        ResetPlatformAdministratorMfaRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(Error.NotFound("Platform administrator was not found."));

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorMfaResetRequestedAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, clock.UtcNow),
            cancellationToken);

        return Result.Success(new ResetPlatformAdministratorMfaResponse(administrator.Id, Implemented: false));
    }
}
