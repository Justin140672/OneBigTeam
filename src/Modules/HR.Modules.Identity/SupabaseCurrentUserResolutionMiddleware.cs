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

        // Tenant resolution moves server-side: once a UserProfile exists (Phase B onwards), the
        // company is resolved from the DB (profile.CompanyId), never trusted from a client-supplied
        // JWT claim. Until Phase B starts creating UserProfile rows, this branch is unreachable in
        // practice (profile is always null today), so the claim-based fallback above is unaffected.
        return new ResolvedCurrentUser(
            UserId: profile.Id,
            Email: profile.Email,
            TenantId: profile.CompanyId.ToString(),
            IsAuthenticated: true);
    }
}