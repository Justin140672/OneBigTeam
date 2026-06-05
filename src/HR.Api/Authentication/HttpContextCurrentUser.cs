using System.Security.Claims;
using HR.SharedKernel;

namespace HR.Api.Authentication;

internal sealed class HttpContextCurrentUser(
    IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var rawUserId = httpContextAccessor.HttpContext?.User.FindFirstValue("sub")
                ?? httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(rawUserId, out var parsedUserId)
                ? parsedUserId
                : null;
        }
    }

    public string? Email => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public string? TenantId => httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id");

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
