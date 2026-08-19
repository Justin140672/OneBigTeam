namespace HR.Modules.Identity.Services;

// Thrown by ISupabaseAuthGateway.CreateUserAsync when Supabase itself reports the email as already
// registered, so callers (SignUpHandler) can surface a clear, specific message instead of the
// generic "registration failed" fallback.
internal sealed class EmailAlreadyRegisteredException(string email)
    : Exception($"An account with this email already exists.")
{
    public string Email { get; } = email;
}
