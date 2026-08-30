using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.RequestPasswordReset;

// Generates a REAL Supabase password-recovery action link (admin generate_link, type=recovery) and
// delivers it via the branded Postmark password-reset template. Always returns success regardless
// of whether a matching UserProfile is found — deliberately avoids leaking which emails are
// registered. If no matching profile exists, no link is generated and no email is sent. A genuine
// failure from the Supabase call (when a profile IS found) is allowed to propagate as a handler
// failure so the caller can surface "something went wrong" rather than a false "check your email".
//
// Never logs the recovery action URL or any recovery token.
internal sealed class RequestPasswordResetHandler(
    IdentityDbContext dbContext,
    ISupabaseAuthGateway supabaseAuthGateway,
    IPasswordResetEmailSender passwordResetEmailSender,
    IConfiguration configuration,
    ILogger<RequestPasswordResetHandler> logger)
{
    public async Task<Result<RequestPasswordResetResponse>> HandleAsync(
        RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Email.ToUpper() == normalizedEmail, cancellationToken);

        if (profile is null)
        {
            logger.LogInformation("Password reset requested for an email with no matching profile; no email sent.");
            return Result.Success(new RequestPasswordResetResponse(true));
        }

        var webBaseUrl =
            configuration["WebApp:BaseUrl"]?.TrimEnd('/') ??
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/reset-password";

        var actionUrl = await supabaseAuthGateway.GenerateRecoveryLinkAsync(profile.Email, redirectTo, cancellationToken);

        var name = $"{profile.FirstName} {profile.LastName}".Trim();

        var emailSent = await passwordResetEmailSender.SendAsync(
            toEmail: profile.Email,
            recipientName: string.IsNullOrWhiteSpace(name) ? null : name,
            actionUrl: actionUrl,
            userAgent: request.UserAgent,
            ct: cancellationToken);

        logger.LogInformation("Password reset email dispatch attempted. EmailSent={EmailSent}", emailSent);

        return Result.Success(new RequestPasswordResetResponse(true));
    }
}
