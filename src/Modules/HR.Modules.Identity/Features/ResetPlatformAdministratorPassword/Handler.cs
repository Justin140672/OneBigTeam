using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;

// Sends a real Supabase password-recovery email via ISupabaseAuthGateway (the same gateway method
// RequestPasswordReset/Handler.cs already uses for regular users) — reuses that handler's exact
// redirect-URL construction/config keys.
internal sealed class ResetPlatformAdministratorPasswordHandler(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ResetPlatformAdministratorPasswordResponse>> HandleAsync(
        ResetPlatformAdministratorPasswordRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<ResetPlatformAdministratorPasswordResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<ResetPlatformAdministratorPasswordResponse>(Error.NotFound("Platform administrator was not found."));

        var webBaseUrl =
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/reset-password";

        await supabaseAuthGateway.RequestPasswordResetAsync(administrator.Email, redirectTo, cancellationToken);

        var now = clock.UtcNow;
        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorPasswordResetRequestedAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new ResetPlatformAdministratorPasswordResponse(administrator.Id, true));
    }
}
