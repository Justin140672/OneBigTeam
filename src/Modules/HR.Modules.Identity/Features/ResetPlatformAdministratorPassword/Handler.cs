using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;

// Generates a REAL single-use Supabase password-recovery action link via the Admin API
// (generate_link, type=recovery) and delivers it through the branded Postmark password-reset
// template — identical to the regular-user RequestPasswordReset/Handler.cs flow. Deliberately NOT
// the client-facing /auth/v1/recover path any more: that let Supabase compose/send the email and
// is being retired across the platform in favour of the admin generate_link approach, so the live
// recovery token is never handled outside a server context.
internal sealed class ResetPlatformAdministratorPasswordHandler(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IPasswordResetEmailSender passwordResetEmailSender,
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
            configuration["WebApp:BaseUrl"]?.TrimEnd('/') ??
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/reset-password";

        var actionUrl = await supabaseAuthGateway.GenerateRecoveryLinkAsync(
            administrator.Email, redirectTo, cancellationToken);

        // Never log actionUrl or any recovery token (mirrors RequestPasswordResetHandler).
        await passwordResetEmailSender.SendAsync(
            toEmail: administrator.Email,
            recipientName: null,
            actionUrl: actionUrl,
            userAgent: null,
            ct: cancellationToken);

        var now = clock.UtcNow;
        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorPasswordResetRequestedAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new ResetPlatformAdministratorPasswordResponse(administrator.Id, true));
    }
}
