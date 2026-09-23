using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Ticket 9: builds the real <see cref="AccountCreationEmailGuard"/> over the real embedded
/// denylist (<see cref="AccountEmailDomainPolicy.Default"/>) for handler tests — deliberately not a
/// fake, so handler tests exercise the actual policy.
/// </summary>
internal static class TestAccountCreationEmailGuard
{
    public static AccountCreationEmailGuard Create(IAuditEventPublisher auditEventPublisher, IClock? clock = null) =>
        new(
            AccountEmailDomainPolicy.Default,
            auditEventPublisher,
            clock ?? new FakeClock(DateTime.UtcNow),
            NullLogger<AccountCreationEmailGuard>.Instance);
}
