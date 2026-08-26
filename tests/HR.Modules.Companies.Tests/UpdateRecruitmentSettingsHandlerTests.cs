using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateRecruitmentSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateRecruitmentSettingsHandlerTests
{
    private static UpdateRecruitmentSettingsRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        VacancyApprovalRequired = true,
        OfferApprovalRequired = true,
        CandidateRetentionDays = 400,
        Version = 1,
    };

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateRecruitmentSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Persists_New_Values_And_Bumps_Version_And_UpdatedAt()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
        var handler = new UpdateRecruitmentSettingsHandler(
            context, new FakeClock(updateTime), new NoOpAuditEventPublisher(), new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.VacancyApprovalRequired);
        Assert.True(result.Value.OfferApprovalRequired);
        Assert.Equal(400, result.Value.CandidateRetentionDays);
        Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), result.Value.UpdatedAt);
        Assert.Equal(2, result.Value.Version);

        var savedSettings = await context.CompanySettings.SingleAsync();
        Assert.True(savedSettings.VacancyApprovalRequired);
        Assert.True(savedSettings.OfferApprovalRequired);
        Assert.Equal(400, savedSettings.CandidateRetentionDays);
    }

    [Fact]
    public async Task HandleAsync_Publishes_RecruitmentSettingsUpdatedAuditEvent_With_Before_And_After_Snapshot()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
        var actorUserId = Guid.NewGuid();
        var handler = new UpdateRecruitmentSettingsHandler(
            context, new FakeClock(updateTime), auditPublisher, new FakeCurrentUser(actorUserId));

        await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        var auditEvt = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<RecruitmentSettingsUpdatedAuditEvent>(auditEvt);
        Assert.Equal(company.Id, auditEvent.CompanyId);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);

        Assert.NotNull(auditEvent.PreviousSettings);
        Assert.False(auditEvent.PreviousSettings!.VacancyApprovalRequired);
        Assert.False(auditEvent.PreviousSettings.OfferApprovalRequired);
        Assert.Equal(730, auditEvent.PreviousSettings.CandidateRetentionDays);

        Assert.True(auditEvent.CurrentSettings.VacancyApprovalRequired);
        Assert.True(auditEvent.CurrentSettings.OfferApprovalRequired);
        Assert.Equal(400, auditEvent.CurrentSettings.CandidateRetentionDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Publishes_No_AuditEvent_When_Version_Is_Stale()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var firstHandler = new UpdateRecruitmentSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var firstResult = await firstHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.Equal(2, firstResult.Value!.Version);

        var auditPublisher = new CapturingAuditEventPublisher();
        var secondHandler = new UpdateRecruitmentSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)),
            auditPublisher,
            new FakeCurrentUser(null));

        var secondResult = await secondHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("conflict", secondResult.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_Default_Settings_Then_Updates_When_Company_Has_No_Existing_Settings_Row()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        // Deliberately no SetSettings() call — company.Settings is null.
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateRecruitmentSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.VacancyApprovalRequired);
        Assert.True(result.Value.OfferApprovalRequired);
        Assert.Equal(400, result.Value.CandidateRetentionDays);

        var savedSettings = await context.CompanySettings.SingleAsync();
        Assert.Equal(company.Id, savedSettings.CompanyId);
        Assert.True(savedSettings.VacancyApprovalRequired);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
