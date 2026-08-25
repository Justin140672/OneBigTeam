using System.Net;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// REP-06: verifies report exports are audited end-to-end through the real
/// DbAuditEventPublisher/AuditDbContext (not just the fake publisher used by the unit tests in
/// HR.Modules.Reporting.Tests) — a successful Sensitive export, a successful Standard export
/// (auditing is uniform across sensitivity, per ReportExportAuditor), a genuine post-authorization
/// failure, and confirmation that a rejected (unauthenticated) attempt never reaches the auditor at
/// all, since <see cref="HR.Modules.Reporting.ReportingAudit"/>'s own doc comment states this event
/// only ever represents a successful or failed *generation* attempt.
/// </summary>
[Collection("Integration")]
public class ReportExportAuditingIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ReportExportAuditingIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> HrClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<HttpClient> RecruiterClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter, companyId);
        return client;
    }

    private async Task<AuditEvent?> FindLatestAuditEventAsync(Guid companyId, string reportId, string eventType)
    {
        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        // MetadataJson is a jsonb column — string.Contains() cannot be translated against it (no
        // `~~` operator for jsonb in Postgres), so filter by the indexed/typed columns in SQL and do
        // the reportId substring check client-side once the (small, test-scoped) result set is
        // materialized.
        var candidates = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == eventType && e.MetadataJson != null)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync();

        return candidates.FirstOrDefault(e => e.MetadataJson!.Contains(reportId));
    }

    /// <summary>
    /// Metadata is serialized via System.Text.Json's default options (PascalCase property names,
    /// possible whitespace formatting) — parse it rather than relying on exact raw-string spacing.
    /// </summary>
    private static JsonElement ParseMetadata(AuditEvent auditEvent) =>
        JsonDocument.Parse(auditEvent.MetadataJson!).RootElement;

    [Fact]
    public async Task Export_Of_A_Sensitive_Report_Persists_A_Success_Audit_Record_Without_Row_Level_Content()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await HrClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auditRecord = await FindLatestAuditEventAsync(companyId, "employee-directory", "report.exported");

        Assert.NotNull(auditRecord);
        Assert.Equal("ReportExport", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.CompanyId);

        var metadata = ParseMetadata(auditRecord);
        Assert.Equal("Sensitive", metadata.GetProperty("Sensitivity").GetString());
        Assert.True(metadata.GetProperty("Success").GetBoolean());

        // No row-level content (e.g. an exported employee's name) can appear in the audit payload —
        // the auditor never has access to the generated report's rows in the first place.
        Assert.DoesNotContain("Smith", auditRecord.AfterJson ?? string.Empty);
        Assert.DoesNotContain("Smith", auditRecord.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task Export_Of_A_Standard_Report_Is_Still_Audited_Consistently_With_A_Sensitive_Report()
    {
        // The implementation audits every export uniformly regardless of sensitivity classification
        // — Sensitivity is recorded on the event for downstream filtering/reporting, but every
        // export (Standard or Sensitive) always produces an audit record.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/recruitment-pipeline-summary/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auditRecord = await FindLatestAuditEventAsync(companyId, "recruitment-pipeline-summary", "report.exported");

        Assert.NotNull(auditRecord);
        var metadata = ParseMetadata(auditRecord!);
        Assert.Equal("Standard", metadata.GetProperty("Sensitivity").GetString());
        Assert.True(metadata.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task Export_That_Fails_After_Authorization_Persists_A_Distinguishable_Failure_Audit_Record()
    {
        // None of the Get*Report handlers backing Export*Report currently return a domain-level
        // Result.Failure (verified by inspection) -- the only reachable post-authorization failure
        // is a thrown exception from the underlying reader, caught by the handler's own try/catch
        // (see ExportHrHeadcountSummaryReportHandler.HandleAsync). This spins up a separate,
        // test-scoped WebApplicationFactory host (standard ASP.NET Core testing technique, disposed
        // at the end of this test) with IHrHeadcountSummaryReader replaced by a throwing fake, so no
        // other test sharing the collection-scoped _factory instance is affected.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using var throwingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IHrHeadcountSummaryReader, ThrowingHrHeadcountSummaryReader>();
            });
        });

        var client = throwingFactory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/hr-headcount-summary/export?format=Csv");

        // The handler's catch block converts the thrown exception into a Result.Failure -> the
        // endpoint surfaces this as a 422/500-class response rather than propagating the exception.
        Assert.False(response.IsSuccessStatusCode);

        var auditRecord = await FindLatestAuditEventAsync(companyId, "hr-headcount-summary", "report.export-failed");

        Assert.NotNull(auditRecord);
        var metadata = ParseMetadata(auditRecord!);
        Assert.False(metadata.GetProperty("Success").GetBoolean());
        Assert.Contains("reader exploded", auditRecord!.MetadataJson);

        // Distinguishable from a successful export: no "report.exported" record was created for this
        // specific failed attempt's company/report combination.
        var successRecord = await FindLatestAuditEventAsync(companyId, "hr-headcount-summary", "report.exported");
        Assert.Null(successRecord);
    }

    [Fact]
    public async Task Unauthorized_Export_Attempt_Is_Rejected_And_Never_Reaches_The_Auditor()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory/export?format=Csv");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var successRecord = await FindLatestAuditEventAsync(companyId, "employee-directory", "report.exported");
        var failureRecord = await FindLatestAuditEventAsync(companyId, "employee-directory", "report.export-failed");

        Assert.Null(successRecord);
        Assert.Null(failureRecord);
    }

    [Fact]
    public async Task Forbidden_Export_Attempt_Is_Rejected_And_Never_Reaches_The_Auditor()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-directory/export?format=Csv");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var successRecord = await FindLatestAuditEventAsync(companyId, "employee-directory", "report.exported");
        var failureRecord = await FindLatestAuditEventAsync(companyId, "employee-directory", "report.export-failed");

        Assert.Null(successRecord);
        Assert.Null(failureRecord);
    }

    private sealed class ThrowingHrHeadcountSummaryReader : IHrHeadcountSummaryReader
    {
        public Task<HrHeadcountSummaryResult> GetHeadcountSummaryAsync(
            Guid companyId, ReportFilterCriteria filter, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("reader exploded");
    }
}
