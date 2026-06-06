using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var resolved = GetResolved();
            if (resolved is not null)
            {
                return resolved.UserId;
            }

            var principal = httpContextAccessor.HttpContext?.User;
            var sub = principal?.FindFirst(CurrentUserClaims.SupabaseUserId)?.Value;
            return Guid.TryParse(sub, out var parsed) ? parsed : null;
        }
    }

    public string? Email
    {
        get
        {
            var resolved = GetResolved();
            if (resolved is not null)
            {
                return resolved.Email;
            }

            return httpContextAccessor.HttpContext?.User
                .FindFirst(CurrentUserClaims.Email)
                ?.Value;
        }
    }

    public string? TenantId
    {
        get
        {
            var resolved = GetResolved();
            if (resolved is not null)
            {
                return resolved.TenantId;
            }

            return httpContextAccessor.HttpContext?.User
                .FindFirst(CurrentUserClaims.TenantId)
                ?.Value;
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            var resolved = GetResolved();
            if (resolved is not null)
            {
                return resolved.IsAuthenticated;
            }

            return httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
        }
    }

    private ResolvedCurrentUser? GetResolved()
    {
        if (httpContextAccessor.HttpContext?.Items.TryGetValue(
                SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey,
                out var currentUser) == true)
        {
            return currentUser as ResolvedCurrentUser;
        }

        return null;
    }
}