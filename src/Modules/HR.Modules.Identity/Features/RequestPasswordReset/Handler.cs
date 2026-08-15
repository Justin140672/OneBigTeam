using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Features.RequestPasswordReset;

// Sends a Supabase password-recovery email for a matching account. Always returns success
// regardless of whether a matching UserProfile is found — deliberately avoids leaking which
// emails are registered (same convention as ResendVerificationHandler). If no matching profile
// exists, the gateway is simply never called; a real failure from the gateway call itself (when a
// profile IS found) is allowed to propagate as a handler failure.
internal sealed class RequestPasswordResetHandler(
    IdentityDbContext dbContext,
    ISupabaseAuthGateway supabaseAuthGateway,
    IConfiguration configuration)
{
    public async Task<Result<RequestPasswordResetResponse>> HandleAsync(
        RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var profile = await dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.Email.ToUpper() == normalizedEmail, cancellationToken);

        if (profile is not null)
        {
            var webBaseUrl =
                configuration["services:web:https:0"] ??
                configuration["services:web:http:0"] ??
                "http://localhost:5157";
            var redirectTo = $"{webBaseUrl}/reset-password";

            await supabaseAuthGateway.RequestPasswordResetAsync(profile.Email, redirectTo, cancellationToken);
        }

        return Result.Success(new RequestPasswordResetResponse(true));
    }
}
