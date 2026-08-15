using HR.Modules.Identity.Services;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.ResetPassword;

// Sets a new password for whichever Supabase account the caller's AccessToken belongs to — that
// token is the short-lived one Supabase issues via the password-recovery email's redirect
// fragment (see RequestPasswordResetHandler/HR.Web's /reset-password), not this app's own JWT
// bearer session, so this endpoint is intentionally anonymous: the AccessToken itself is what
// authenticates the caller to Supabase, verified by Supabase's own /auth/v1/user call, not by us.
internal sealed class ResetPasswordHandler(ISupabaseAuthGateway supabaseAuthGateway)
{
    public async Task<Result<ResetPasswordResponse>> HandleAsync(
        ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await supabaseAuthGateway.UpdatePasswordAsync(request.AccessToken, request.NewPassword, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Most likely cause: the recovery token has expired or was already used — surfaced as
            // a validation error rather than a 500, since it's a normal/expected user-facing
            // outcome ("this link is invalid or has expired"), not a server fault.
            return Result.Failure<ResetPasswordResponse>(Error.Validation(ex.Message));
        }

        return Result.Success(new ResetPasswordResponse(true));
    }
}
