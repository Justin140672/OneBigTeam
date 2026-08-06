using HR.Modules.Identity.Services;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeSupabaseAuthGateway : ISupabaseAuthGateway
{
    public List<(string Email, string RedirectTo)> CreatedUsers { get; } = [];
    public List<(string Email, string RedirectTo)> ResentEmails { get; } = [];

    public Guid? UserIdToReturn { get; set; }
    public bool ShouldThrowOnCreate { get; set; }
    public bool ShouldThrowOnExchange { get; set; }
    public bool ShouldThrowOnSignIn { get; set; }

    public List<(string Email, string Password)> EnsuredDevUsers { get; } = [];
    public List<(string Email, string Password)> SignedInUsers { get; } = [];

    public Task<Guid> CreateUserAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        CreatedUsers.Add((email, redirectTo));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        ResentEmails.Add((email, redirectTo));
        return Task.CompletedTask;
    }

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnExchange)
        {
            throw new InvalidOperationException("Simulated invalid/expired Supabase verification code.");
        }

        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        EnsuredDevUsers.Add((email, password));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnSignIn)
        {
            throw new InvalidOperationException("Simulated Supabase sign-in failure.");
        }

        SignedInUsers.Add((email, password));
        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }
}
