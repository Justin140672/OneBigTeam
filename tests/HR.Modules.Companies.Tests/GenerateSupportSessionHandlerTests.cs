using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GenerateSupportSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class GenerateSupportSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var companyId = await SeedCompanyAsync(context);

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new GenerateSupportSessionRequest(companyId, "Investigating a customer-reported issue."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new GenerateSupportSessionRequest(Guid.NewGuid(), "Investigating a customer-reported issue."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Issues_Session_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = await SeedCompanyAsync(context);

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new GenerateSupportSessionRequest(companyId, "Investigating a customer-reported issue."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.NotEqual(Guid.Empty, result.Value.SupportSessionId);
        Assert.Equal(Now.AddMinutes(20), result.Value.ExpiresAt);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Token));

        var persisted = await context.SupportSessions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(result.Value.SupportSessionId, persisted.Id);
        Assert.Equal(actorId, persisted.IssuedByAdminUserId);
        Assert.Equal("admin@example.com", persisted.IssuedByAdminEmail);
        Assert.NotEqual(result.Value.Token, persisted.TokenHash);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<SupportSessionGeneratedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(persisted.Id, auditEvent.SupportSessionId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal("Investigating a customer-reported issue.", auditEvent.Reason);
    }

    [Fact]
    public async Task HandleAsync_Generates_A_Unique_Token_Each_Call()
    {
        await using var context = BuildContext();
        var companyId = await SeedCompanyAsync(context);

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new CapturingAuditEventPublisher());

        var first = await handler.HandleAsync(
            new GenerateSupportSessionRequest(companyId, "Investigating a customer-reported issue."),
            CancellationToken.None);
        var second = await handler.HandleAsync(
            new GenerateSupportSessionRequest(companyId, "Investigating a customer-reported issue."),
            CancellationToken.None);

        Assert.NotEqual(first.Value!.Token, second.Value!.Token);
    }

    private static async Task<Guid> SeedCompanyAsync(CompaniesDbContext context)
    {
        var company = Company.Create(Guid.NewGuid(), "Acme Ltd", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();
        return company.Id;
    }

    private static GenerateSupportSessionHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
    {
        return new GenerateSupportSessionHandler(
            context,
            currentUser,
            configuration,
            new FakeClock(Now.UtcDateTime),
            auditEventPublisher);
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            builder.AddInMemoryCollection(data);
        }
        else
        {
            builder.AddInMemoryCollection();
        }

        return builder.Build();
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
