using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Features.SendInvite;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SendInviteEndpoint = HR.Modules.Identity.Features.SendInvite.Endpoint;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// CodeQL alert #61 (exposure of private information): Identity operational logs must never carry
/// a submitted or recipient email address — not in the formatted message, the structured state,
/// a log scope, or the text/properties of a logged exception — and must not substitute a partially
/// masked address either. The business audit trail (a separate, restricted store) is unaffected.
/// Companion to <see cref="SensitiveAuthLoggingTests"/>; uses <see cref="CapturingLogger{T}"/>,
/// which inspects every one of those channels.
/// </summary>
[Collection("IdentityDatabase")]
public class IdentityOperationalLogEmailExposureTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private const string PrivateDomain = "northwind-codeql61.com";

    private static void AssertNoEmailLeak(string capturedText, string email)
    {
        var localPart = email[..email.IndexOf('@')];

        Assert.DoesNotContain(email, capturedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(localPart, capturedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PrivateDomain, capturedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("***@", capturedText, StringComparison.Ordinal);
    }

    private async Task SeedOwnerAsync(string ownerEmail)
    {
        await using var db = fixture.BuildContext();
        db.PlatformAdministrators.Add(
            PlatformAdministrator.Create(ownerEmail, PlatformAdministratorRole.PlatformOwner, Now));
        await db.SaveChangesAsync();
    }

    private CreatePlatformAdministratorHandler BuildCreateHandler(
        FakeSupabaseAuthGateway gateway, FakeAuditEventPublisher auditPublisher, ILogger<CreatePlatformAdministratorHandler> logger) =>
        new(fixture.BuildContext(), gateway, Clock, new ConfigurationBuilder().Build(), auditPublisher,
            TestAccountCreationEmailGuard.Create(auditPublisher, Clock), logger);

    [Fact]
    public async Task CreatePlatformAdministrator_Provider_Lookup_Failure_Does_Not_Log_Submitted_Email_Anywhere()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var submittedEmail = $"Private.Admin.{Guid.NewGuid():N}@{PrivateDomain}";
        var normalizedEmail = submittedEmail.ToLowerInvariant();

        // A provider failure that echoes the address in its message, inner exception and Data —
        // the worst case the handler must not forward to the log.
        var gateway = new FakeSupabaseAuthGateway
        {
            GetUserIdByEmailFailure = email =>
            {
                var ex = new InvalidOperationException(
                    $"Supabase list-users failed for filter={email}",
                    new HttpRequestException($"GET /auth/v1/admin/users?filter={email} failed"));
                ex.Data["email"] = email;
                return ex;
            },
        };
        var logger = new CapturingLogger<CreatePlatformAdministratorHandler>();
        var actorId = Guid.NewGuid();

        Result<CreatePlatformAdministratorResponse> result;
        using (logger.BeginScope(new Dictionary<string, object?> { ["Operation"] = "CreatePlatformAdministrator" }))
        {
            result = await BuildCreateHandler(gateway, new FakeAuditEventPublisher(), logger).HandleAsync(
                new CreatePlatformAdministratorRequest(submittedEmail, PlatformAdministratorRole.SupportStaff),
                new FakeCurrentUser(actorId, ownerEmail),
                CancellationToken.None);
        }

        Assert.True(result.IsFailure);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        AssertNoEmailLeak(logger.AllText, normalizedEmail);
        Assert.Null(entry.ExceptionText);

        Assert.Equal("provider_account_lookup", entry.StateValue("FailureStage"));
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.StateValue("ExceptionType"));
        Assert.True(Guid.TryParse(entry.StateValue("CorrelationId"), out var correlationId) && correlationId != Guid.Empty);
        Assert.Equal(actorId.ToString(), entry.StateValue("ActorUserId"));
        Assert.Contains("CreatePlatformAdministrator", entry.Message);
    }

    [Fact]
    public async Task CreatePlatformAdministrator_Provisioning_Failure_Logs_Record_Id_But_Not_Email_And_Keeps_Audit_Email()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var submittedEmail = $"provision.admin.{Guid.NewGuid():N}@{PrivateDomain}";
        var gateway = new FakeSupabaseAuthGateway
        {
            CreatePendingUserFailure = email =>
                new InvalidOperationException($"Supabase rejected user {email}: email_address_invalid"),
        };
        var auditPublisher = new FakeAuditEventPublisher();
        var logger = new CapturingLogger<CreatePlatformAdministratorHandler>();

        var result = await BuildCreateHandler(gateway, auditPublisher, logger).HandleAsync(
            new CreatePlatformAdministratorRequest(submittedEmail, PlatformAdministratorRole.SupportStaff),
            new FakeCurrentUser(Guid.NewGuid(), ownerEmail),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, result.Value.ProvisioningStatus);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        AssertNoEmailLeak(logger.AllText, submittedEmail);
        Assert.Null(entry.ExceptionText);
        Assert.Equal(result.Value.Id.ToString(), entry.StateValue("AdministratorId"));
        Assert.Equal("provider_account_creation_failed", entry.StateValue("FailureStage"));
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.StateValue("ExceptionType"));
        Assert.True(Guid.TryParse(entry.StateValue("CorrelationId"), out _));

        // The restricted business audit trail deliberately still records the address.
        var audit = Assert.Single(auditPublisher.PublishedEvents.OfType<PlatformAdministratorProvisioningFailedAuditEvent>());
        Assert.Equal(submittedEmail, audit.Email);
    }

    [Fact]
    public async Task SendInvite_Delivery_Failure_Logs_Employee_Id_But_Not_Recipient_Address()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recipientEmail = $"invitee.{Guid.NewGuid():N}@{PrivateDomain}";

        var auditPublisher = new FakeAuditEventPublisher();
        var logger = new CapturingLogger<SendInviteEndpoint>();
        var emailSender = new FakeInvitationEmailSender(succeeds: false);

        var endpoint = Factory.Create<SendInviteEndpoint>(
            ctx => ctx.RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
            fixture.BuildContext(),
            Clock,
            emailSender,
            new FakeInviteLinkBuilder(),
            TestAccountCreationEmailGuard.Create(auditPublisher, Clock),
            FakeCurrentUser.Authenticated(Guid.NewGuid(), companyId.ToString()),
            logger);

        await endpoint.HandleAsync(
            new SendInviteRequest { CompanyId = companyId, EmployeeId = employeeId, Email = recipientEmail },
            CancellationToken.None);

        Assert.Single(emailSender.Sent, s => s.ToEmail == recipientEmail);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        AssertNoEmailLeak(logger.AllText, recipientEmail);
        Assert.Null(entry.StateValue("To"));
        Assert.Null(entry.StateValue("Email"));
        Assert.Equal(employeeId.ToString(), entry.StateValue("EmployeeId"));
        Assert.Equal(companyId.ToString(), entry.StateValue("CompanyId"));
        Assert.True(Guid.TryParse(entry.StateValue("InviteId"), out _));
    }
}
