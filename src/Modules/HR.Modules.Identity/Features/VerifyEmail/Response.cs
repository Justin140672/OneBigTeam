namespace HR.Modules.Identity.Features.VerifyEmail;

// The caller (HR.Web's /verify-email callback) already holds the Supabase access token itself —
// it's what authenticated this very request — so there's nothing to hand back except confirmation
// of who was resolved and which company was (or already was) activated.
internal sealed record VerifyEmailResponse(Guid UserId, Guid CompanyId);
