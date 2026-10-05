using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateWorkEmailSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateWorkEmailSettingsHandlerTests
{
    private static readonly DateTime UpdateTime = new(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);

    private static UpdateWorkEmailSettingsRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        SuggestionsEnabled = true,
        PrimaryDomain = "@Example.COM",
        NamingConvention = WorkEmailNamingConvention.FirstInitialDotLastName,
        Version = 1,
    };

    private static async Task<Company> SeedCompanyAsync(CompaniesDbContext context, bool withSettings = true)
    {
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        if (withSettings)
            company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();
        return company;
    }

    private static UpdateWorkEmailSettingsHandler BuildHandler(
        CompaniesDbContext context,
        IAuditEventPublisher? publisher = null,
        Guid? actorUserId = null) =>
        new(context, new FakeClock(UpdateTime), publisher ?? new NoOpAuditEventPublisher(), new FakeCurrentUser(actorUserId));

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(ValidRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Persists_Normalised_Values_And_Bumps_Version_And_UpdatedAt()
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context);

        var result = await BuildHandler(context).HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SuggestionsEnabled);
        Assert.Equal("example.com", result.Value.PrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstInitialDotLastName, result.Value.NamingConvention);
        Assert.Equal(new DateTimeOffset(UpdateTime, TimeSpan.Zero), result.Value.UpdatedAt);
        Assert.Equal(2, result.Value.Version);

        var saved = await context.CompanySettings.SingleAsync();
        Assert.True(saved.WorkEmailSuggestionsEnabled);
        Assert.Equal("example.com", saved.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstInitialDotLastName, saved.WorkEmailNamingConvention);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_Before_And_After_Snapshot()
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context);
        var publisher = new CapturingAuditEventPublisher();
        var actorUserId = Guid.NewGuid();

        await BuildHandler(context, publisher, actorUserId).HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        var auditEvent = Assert.IsType<WorkEmailSettingsUpdatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(company.Id, auditEvent.CompanyId);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("work-email-settings.updated", ((IAuditEvent)auditEvent).EventType);

        Assert.NotNull(auditEvent.PreviousSettings);
        Assert.True(auditEvent.PreviousSettings!.SuggestionsEnabled);
        Assert.Null(auditEvent.PreviousSettings.PrimaryDomain);

        Assert.True(auditEvent.CurrentSettings.SuggestionsEnabled);
        Assert.Equal("example.com", auditEvent.CurrentSettings.PrimaryDomain);
        Assert.Equal(nameof(WorkEmailNamingConvention.FirstInitialDotLastName), auditEvent.CurrentSettings.NamingConvention);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Publishes_No_Audit_Event_When_Version_Is_Stale()
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context);

        var first = await BuildHandler(context).HandleAsync(ValidRequest(company.Id), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var publisher = new CapturingAuditEventPublisher();
        var second = await BuildHandler(context, publisher).HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("conflict", second.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_Default_Settings_Then_Updates_When_Company_Has_No_Settings_Row()
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context, withSettings: false);
        var publisher = new CapturingAuditEventPublisher();

        var result = await BuildHandler(context, publisher).HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.CompanySettings.SingleAsync();
        Assert.Equal(company.Id, saved.CompanyId);
        Assert.Equal("example.com", saved.WorkEmailPrimaryDomain);

        var auditEvent = Assert.IsType<WorkEmailSettingsUpdatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Null(auditEvent.PreviousSettings);
    }

    [Fact]
    public async Task HandleAsync_Can_Disable_Suggestions_While_Keeping_Primary_Domain()
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context);
        var enabled = await BuildHandler(context).HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        var result = await BuildHandler(context).HandleAsync(
            new UpdateWorkEmailSettingsRequest
            {
                CompanyId = company.Id,
                SuggestionsEnabled = false,
                PrimaryDomain = "example.com",
                NamingConvention = WorkEmailNamingConvention.FirstName,
                Version = enabled.Value!.Version,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SuggestionsEnabled);
        Assert.Equal("example.com", result.Value.PrimaryDomain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a domain")]
    public async Task HandleAsync_Rejects_Missing_Primary_Domain_And_Keeps_Existing_Settings(string? primaryDomain)
    {
        await using var context = BuildContext();
        var company = await SeedCompanyAsync(context);
        var enabled = await BuildHandler(context).HandleAsync(ValidRequest(company.Id), CancellationToken.None);
        var publisher = new CapturingAuditEventPublisher();

        var result = await BuildHandler(context, publisher).HandleAsync(
            ValidRequest(company.Id) with { PrimaryDomain = primaryDomain, Version = enabled.Value!.Version },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);

        var saved = await context.CompanySettings.SingleAsync();
        Assert.Equal("example.com", saved.WorkEmailPrimaryDomain);
        Assert.Equal(enabled.Value.Version, saved.Version);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
