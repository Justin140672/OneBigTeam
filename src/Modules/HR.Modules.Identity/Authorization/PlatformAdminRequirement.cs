using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Authorization;

// Requirement for the "platform:admin" policy — succeeds only when the caller matches an
// enabled row in identity.platform_administrators. See PlatformAdminAuthorizationHandler for
// the actual DB-backed matching logic (by SupabaseAuthUserId, falling back to email).
internal sealed class PlatformAdminRequirement : IAuthorizationRequirement
{
}
