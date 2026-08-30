namespace HR.Modules.Identity.Features.RequestPasswordReset;

// UserAgent is the raw browser User-Agent forwarded by HR.Web from the forgot-password form
// submission. It is used only to render friendly browser/OS values in the reset email and is never
// a security control. Optional — a missing value simply renders as "Unknown".
internal sealed record RequestPasswordResetRequest(string Email, string? UserAgent = null);
