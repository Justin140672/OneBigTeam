namespace HR.Infrastructure.Email;

internal sealed class PostmarkOptions
{
    public string ServerToken { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string? MessageStream { get; set; } = "outbound";
    public string InvitationTemplateAlias { get; set; } = "user-invitation";
    public string PasswordResetTemplateAlias { get; set; } = "password-reset";
}
