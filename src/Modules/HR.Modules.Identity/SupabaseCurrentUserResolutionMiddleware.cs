using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity;

internal sealed class SupabaseCurrentUserResolutionMiddleware(RequestDelegate next)
{
    public const string CurrentUserItemKey = "__identity_current_user";

    public async Task InvokeAsync(HttpContext context, IdentityDbContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var resolved = await ResolveAsync(context, dbContext);
            context.Items[CurrentUserItemKey] = resolved;
        }

        await next(context);
    }

    private static async Task<ResolvedCurrentUser> ResolveAsync(HttpContext context, IdentityDbContext dbContext)
    {
        var supabaseUserIdRaw = context.User.FindFirst(CurrentUserClaims.SupabaseUserId)?.Value;
        var email = context.User.FindFirst(CurrentUserClaims.Email)?.Value;
        var tenantId = context.User.FindFirst(CurrentUserClaims.TenantId)?.Value;

        // "Login as Customer" support session (see HR.Modules.Companies.Domain.SupportSession /
        // HR.Infrastructure's support-session JWT issuer). This identity is deliberately never
        // resolved against identity.user_profiles — it must NOT be able to satisfy any
        // employee-ownership authorization rule, and the token's "sub" claim is a randomly
        // generated marker that is guaranteed not to match any real Supabase user id. The tenant
        // (target company) comes directly from the token's own company_id claim, which was set
        // server-side when the token was minted (HR.Modules.Companies.RedeemSupportSession), never
        // from anything client-supplied at request time.
        var supportSessionIdRaw = context.User.FindFirst(CurrentUserClaims.SupportSessionId)?.Value;
        if (Guid.TryParse(supportSessionIdRaw, out var supportSessionId))
        {
            return new ResolvedCurrentUser(
                UserId: Guid.TryParse(supabaseUserIdRaw, out var marker) ? marker : null,
                Email: email,
                TenantId: tenantId,
                IsAuthenticated: true,
                IsSupportSession: true,
                SupportSessionId: supportSessionId);
        }

        if (!Guid.TryParse(supabaseUserIdRaw, out var supabaseUserId))
        {
            return new ResolvedCurrentUser(
                UserId: null,
                Email: email,
                TenantId: tenantId,
                IsAuthenticated: true);
        }

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.SupabaseAuthUserId == supabaseUserId, context.RequestAborted);

        if (profile is null)
        {
            return new ResolvedCurrentUser(
                UserId: supabaseUserId,
                Email: email,
                TenantId: tenantId,
                IsAuthenticated: true);
        }

        return new ResolvedCurrentUser(
            UserId: profile.Id,
            Email: profile.Email,
            TenantId: profile.CompanyId.ToString(),
            IsAuthenticated: true);
    }
}