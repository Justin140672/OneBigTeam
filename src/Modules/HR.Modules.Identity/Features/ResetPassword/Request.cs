namespace HR.Modules.Identity.Features.ResetPassword;

internal sealed record ResetPasswordRequest(string AccessToken, string NewPassword);
