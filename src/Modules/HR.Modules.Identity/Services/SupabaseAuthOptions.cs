namespace HR.Modules.Identity.Services;

internal sealed class SupabaseAuthOptions
{
    public string ProjectUrl { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string JwksUrl { get; set; } = "";
}
