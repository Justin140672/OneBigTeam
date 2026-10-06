using System.IdentityModel.Tokens.Jwt;

using HR.Infrastructure.Abstractions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace HR.Api.Authentication;

/// <summary>
/// P1 "Login as Customer": wiring for the second JwtBearer scheme that validates support-session
/// tokens minted by HR.Infrastructure.Security.SupportSessionTokenIssuer. Deliberately a distinct
/// scheme/issuer/signing key from the real Supabase Bearer scheme (SupabaseJwtBearerConfiguration)
/// — a policy scheme (see Program.cs) forwards each incoming request to whichever scheme matches
/// its token's unvalidated "iss" claim, so both authentication paths can coexist behind a single
/// [Authorize]/Policies(...) surface without weakening the real Supabase validation path at all.
/// </summary>
public static class SupportSessionJwtBearerConfiguration
{
    public const string SchemeName = "SupportSession";

    public static void ConfigureValidation(JwtBearerOptions options, IConfiguration configuration)
    {
        options.MapInboundClaims = false;

        var key = SupportSessionTokenConstants.ResolveSigningKey(configuration);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = SupportSessionTokenConstants.Issuer,
            ValidateAudience = true,
            ValidAudience = SupportSessionTokenConstants.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = key,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidatePersistedStateAsync,
        };
    }


    public static async Task ValidatePersistedStateAsync(TokenValidatedContext context)
    {
        try
        {
            var principal = context.Principal;
            var validator = context.HttpContext.RequestServices.GetRequiredService<ISupportSessionStateValidator>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HR.Api.Authentication.SupportSession");

            if (principal is null
                || !Guid.TryParse(principal.FindFirst("support_session_id")?.Value, out var supportSessionId)
                || !Guid.TryParse(principal.FindFirst("company_id")?.Value, out var companyId)
                || !Guid.TryParse(principal.FindFirst("sub")?.Value, out var adminUserId))
            {
                context.Fail("Support session token is missing required claims.");
                return;
            }

            var active = await validator.IsActiveAsync(
                supportSessionId,
                companyId,
                adminUserId,
                principal.FindFirst("email")?.Value,
                context.HttpContext.RequestAborted);

            if (!active)
            {
                logger.LogWarning("Rejected support session token for session {SupportSessionId}: persisted state is not active.", supportSessionId);
                context.Fail("Support session is not active.");
            }
        }
        catch (Exception)
        {
            context.Fail("Support session state could not be verified.");
        }
    }

    public static bool LooksLikeSupportSessionToken(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(bearerToken))
                return false;

            var token = handler.ReadJwtToken(bearerToken);
            return string.Equals(token.Issuer, SupportSessionTokenConstants.Issuer, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
