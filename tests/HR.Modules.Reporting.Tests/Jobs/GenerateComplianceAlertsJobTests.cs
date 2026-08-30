using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

public class GenerateComplianceAlertsJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static ExpiringEmployeeDocumentItem Doc(
        DateOnly expiry,
        ComplianceDocumentKind kind = ComplianceDocumentKind.Immigration,
        Guid? employeeId = null) =>
        new(employeeId ?? Guid.NewGuid(), "Doc", "Type", expiry, kind);

    private static ProbationReviewComplianceItem Review(DateOnly dueDate, Guid? employeeId = null) =>
        new(employeeId ?? Guid.NewGuid(), Guid.NewGuid(), "ManagerCheckIn", dueDate);

    private static GetComplianceCentreHandler Handler(
        IExpiringEmployeeDocumentReader? expiring = null,
        IReadOnlyList<ProbationReviewComplianceItem>? reviews = null) =>
        new(
            expiring ?? new FakeExpiringEmployeeDocumentReader(),
            new FakeDocumentComplianceReportReader([]),
            new FakeOutstandingDocumentRequestComplianceReader(),
            new FakeProbationReviewComplianceReader(reviews),
            new FakeEmployeeDirectoryReader([]),
            new FakeClock(FixedUtcNow));

    private static GenerateComplianceAlertsJob Job(
        IActiveCompanyDirectory directory,
        GetComplianceCentreHandler handler,
        CapturingAdministrativeAlertWriter writer) =>
        new(directory, handler, writer, new FakeClock(FixedUtcNow),
            NullLogger<GenerateComplianceAlertsJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_Raises_One_Alert_Per_Overdue_Category()
    {
        var companyId = Guid.NewGuid();
        var writer = new CapturingAdministrativeAlertWriter();
        var handler = Handler(
            expiring: new FakeExpiringEmployeeDocumentReader([Doc(Today.AddDays(-3), ComplianceDocumentKind.Immigration)]),
            reviews: [Review(Today.AddDays(-2))]);

        await Job(new FakeActiveCompanyDirectory(companyId), handler, writer).ExecuteAsync();

        Assert.Equal(2, writer.Commands.Count);
        Assert.All(writer.Commands, c =>
        {
            Assert.Equal(companyId, c.CompanyId);
            Assert.Equal(AdministrativeAlertCategory.Compliance, c.Category);
            Assert.Equal(AdministrativeAlertSeverity.Warning, c.Severity);
            Assert.EndsWith($"/companies/{companyId}/reporting/compliance-centre", c.ActionUrl);
            Assert.Contains("1 overdue", c.Summary);
        });

        Assert.Contains(writer.Commands, c => c.DedupKey == "compliance:ExpiringVisa");
        Assert.Contains(writer.Commands, c => c.DedupKey == "compliance:ProbationReview");
    }

    [Fact]
    public async Task ExecuteAsync_Raises_Nothing_When_No_Overdue_Items()
    {
        var writer = new CapturingAdministrativeAlertWriter();
        var handler = Handler(
            expiring: new FakeExpiringEmployeeDocumentReader([Doc(Today.AddDays(10))]),
            reviews: [Review(Today.AddDays(10))]);

        await Job(new FakeActiveCompanyDirectory(Guid.NewGuid()), handler, writer).ExecuteAsync();

        Assert.Empty(writer.Commands);
    }

    [Fact]
    public async Task ExecuteAsync_Re_Run_Issues_Same_Dedup_Keyed_Commands_Again()
    {
        var companyId = Guid.NewGuid();
        var writer = new CapturingAdministrativeAlertWriter();
        var handler = Handler(
            expiring: new FakeExpiringEmployeeDocumentReader([Doc(Today.AddDays(-3), ComplianceDocumentKind.Immigration)]),
            reviews: [Review(Today.AddDays(-2))]);
        var job = Job(new FakeActiveCompanyDirectory(companyId), handler, writer);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Equal(4, writer.Commands.Count);
        Assert.Equal(2, writer.Commands.Count(c => c.DedupKey == "compliance:ExpiringVisa"));
        Assert.Equal(2, writer.Commands.Count(c => c.DedupKey == "compliance:ProbationReview"));
    }

    [Fact]
    public async Task ExecuteAsync_Isolates_One_Company_Failure_From_The_Batch()
    {
        var failingCompany = Guid.NewGuid();
        var healthyCompany = Guid.NewGuid();
        var writer = new CapturingAdministrativeAlertWriter();
        var handler = Handler(
            expiring: new ThrowingForCompanyExpiringEmployeeDocumentReader(
                failingCompany,
                [Doc(Today.AddDays(-3), ComplianceDocumentKind.Immigration)]),
            reviews: null);

        var job = Job(
            new FakeActiveCompanyDirectory(failingCompany, healthyCompany), handler, writer);

        await job.ExecuteAsync();

        var command = Assert.Single(writer.Commands);
        Assert.Equal(healthyCompany, command.CompanyId);
        Assert.Equal("compliance:ExpiringVisa", command.DedupKey);
    }

    // Test 5 (handler Result.Failure is skipped gracefully) is intentionally omitted:
    // GetComplianceCentreHandler.HandleAsync always returns Result.Success — it has no failure
    // path reachable through its readers, so there is no way to exercise the job's IsFailure branch
    // without mocking the concrete handler, which the task forbids.

    private sealed class ThrowingForCompanyExpiringEmployeeDocumentReader(
        Guid throwForCompanyId,
        IReadOnlyList<ExpiringEmployeeDocumentItem> items) : IExpiringEmployeeDocumentReader
    {
        public Task<IReadOnlyList<ExpiringEmployeeDocumentItem>> GetExpiringEmployeeDocumentsAsync(
            Guid companyId, DateOnly asOf, int lookaheadDays, CancellationToken cancellationToken)
        {
            if (companyId == throwForCompanyId)
                throw new InvalidOperationException("reader boom for company " + companyId);

            return Task.FromResult(items);
        }
    }
}
