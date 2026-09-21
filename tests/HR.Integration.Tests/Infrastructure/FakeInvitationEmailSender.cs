using HR.SharedKernel;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IInvitationEmailSender"/> (the branded-template invitation path
/// introduced by the Postmark user-invitation integration). Records the send into the shared
/// <see cref="FakeEmailSender"/> so existing assertions over <c>_factory.EmailSender.Sent</c>
/// continue to work without every invitation test needing a second capture surface.
///
/// Registered as a singleton shared by every test class in the "Integration" collection (see
/// ApiWebApplicationFactory), and the whole assembly runs with
/// <c>CollectionBehavior(DisableTestParallelization = true)</c> (AssemblyInfo.cs), so tests run
/// strictly sequentially — there is no need for this fake's mutable failure state to be
/// thread-safe against concurrent test execution. It only needs a lock to guard against
/// reentrancy from within a single test's own concurrent sends (e.g. ProcessInvitationBatchJob
/// processing recipients). By default every send still succeeds, matching the original
/// always-true behaviour other tests rely on. Tests that opt a specific recipient into failure
/// via <see cref="FailFor"/> MUST call <see cref="Reset"/> (e.g. in a try/finally) once done, so
/// the failure state never leaks into a later test sharing this singleton.
/// </summary>
public sealed class FakeInvitationEmailSender(FakeEmailSender emailSender) : IInvitationEmailSender
{
    // Mirrors the subject the pre-template inline invitation email used, which the integration
    // tests assert on.
    public const string Subject = "You have been invited to One Big Team";

    private readonly object _lock = new();
    private readonly HashSet<string> _failingEmails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks the given recipient email address as one whose next <see cref="SendAsync"/> call(s)
    /// should report failure (return <c>false</c>), simulating a real delivery failure without
    /// actually contacting an email provider. Remember to call <see cref="Reset"/> afterward.
    /// </summary>
    public void FailFor(string email)
    {
        lock (_lock) { _failingEmails.Add(email); }
    }

    /// <summary>
    /// Clears all configured failures, restoring the default always-succeeds behaviour. Call this
    /// after any test that used <see cref="FailFor"/>, since this fake is a singleton shared
    /// across the whole "Integration" collection.
    /// </summary>
    public void Reset()
    {
        lock (_lock) { _failingEmails.Clear(); }
    }

    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default)
    {
        var html = $"<p>You have been invited. <a href=\"{actionUrl}\">Accept your invite</a></p>";
        emailSender.SendAsync(toEmail, Subject, html, ct);

        bool shouldFail;
        lock (_lock) { shouldFail = _failingEmails.Contains(toEmail); }

        return Task.FromResult(!shouldFail);
    }
}
