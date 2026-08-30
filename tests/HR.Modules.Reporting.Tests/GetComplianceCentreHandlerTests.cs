using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetComplianceCentreHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static ExpiringEmployeeDocumentItem Doc(
        Guid? employeeId = null,
        ComplianceDocumentKind kind = ComplianceDocumentKind.Other,
        DateOnly? expiry = null,
        string title = "Doc",
        string typeName = "Type") =>
        new(employeeId ?? Guid.NewGuid(), title, typeName, expiry ?? Today.AddDays(5), kind);

    private static DocumentComplianceReportItem Missing(Guid employeeId, params string[] missingTypeNames) =>
        new(employeeId, Guid.NewGuid(), RequiredCount: missingTypeNames.Length, UploadedCount: 0,
            MissingCount: missingTypeNames.Length, ExpiringSoonCount: 0, ExpiredCount: 0, missingTypeNames);

    private static OutstandingDocumentRequestComplianceItem Request(
        Guid? employeeId = null, DateOnly? dueDate = null, bool isMandatory = false, string typeName = "Passport") =>
        new(Guid.NewGuid(), employeeId ?? Guid.NewGuid(), typeName, dueDate, isMandatory);

    private static ProbationReviewComplianceItem Review(
        Guid? employeeId = null, DateOnly? dueDate = null, string reviewType = "ManagerCheckIn") =>
        new(employeeId ?? Guid.NewGuid(), Guid.NewGuid(), reviewType, dueDate ?? Today.AddDays(5));

    private static EmployeeDirectoryReportItem DirectoryItem(
        Guid employeeId, string name = "Employee", string department = "Engineering") =>
        new(employeeId, "EMP-001", name, department, "Engineer", "Manager",
            "Full-Time", new DateOnly(2026, 1, 1), "Active", "London", "employee@example.com");

    private static GetComplianceCentreHandler MakeHandler(
        IReadOnlyList<ExpiringEmployeeDocumentItem>? expiring = null,
        IReadOnlyList<DocumentComplianceReportItem>? compliance = null,
        IReadOnlyList<OutstandingDocumentRequestComplianceItem>? requests = null,
        IReadOnlyList<ProbationReviewComplianceItem>? reviews = null,
        IReadOnlyList<EmployeeDirectoryReportItem>? directory = null) =>
        new(
            new FakeExpiringEmployeeDocumentReader(expiring),
            new FakeDocumentComplianceReportReader(compliance ?? []),
            new FakeOutstandingDocumentRequestComplianceReader(requests),
            new FakeProbationReviewComplianceReader(reviews),
            new FakeEmployeeDirectoryReader(directory ?? []),
            new FakeClock(FixedUtcNow));

    private static GetComplianceCentreRequest Req(
        string? category = null, string? severity = null, string? department = null,
        Guid? managerId = null, DateOnly? dueStart = null, DateOnly? dueEnd = null) =>
        new(CompanyId, category, department, managerId, dueStart, dueEnd, severity);

    // ── severity boundaries ─────────────────────────────────────────────────

    [Theory]
    [InlineData(-1, "Overdue")]
    [InlineData(0, "DueSoon")]
    [InlineData(30, "DueSoon")]
    [InlineData(31, "Informational")]
    public async Task HandleAsync_Computes_Severity_At_Boundaries(int dueOffsetDays, string expectedSeverity)
    {
        var handler = MakeHandler(expiring: [Doc(expiry: Today.AddDays(dueOffsetDays))]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedSeverity, row.Severity);
    }

    [Fact]
    public async Task HandleAsync_Drops_Probation_Reviews_Beyond_Horizon()
    {
        var withinEmp = Guid.NewGuid();
        var handler = MakeHandler(reviews:
        [
            Review(withinEmp, Today.AddDays(30)),
            Review(Guid.NewGuid(), Today.AddDays(31)),
        ]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(withinEmp, row.EmployeeId);
        Assert.Equal("DueSoon", row.Severity);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_Overdue_Probation_Review()
    {
        var handler = MakeHandler(reviews: [Review(dueDate: Today.AddDays(-1))]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", row.Severity);
        Assert.Equal("ProbationReview", row.Category);
    }

    // ── kind -> category mapping ────────────────────────────────────────────

    [Theory]
    [InlineData(ComplianceDocumentKind.Immigration, "ExpiringVisa")]
    [InlineData(ComplianceDocumentKind.Certification, "ExpiringCertification")]
    [InlineData(ComplianceDocumentKind.Other, "ExpiringOtherDocument")]
    public async Task HandleAsync_Maps_DocumentKind_To_Category(ComplianceDocumentKind kind, string expectedCategory)
    {
        var handler = MakeHandler(expiring: [Doc(kind: kind)]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedCategory, row.Category);
    }

    // ── missing required documents ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Emits_One_Informational_Row_Per_Missing_Document_Type()
    {
        var employeeId = Guid.NewGuid();
        var handler = MakeHandler(compliance: [Missing(employeeId, "Passport", "Right to Work")]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var rows = result.Value!.Items;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("MissingRequiredDocument", r.Category);
            Assert.Equal("Informational", r.Severity);
            Assert.Null(r.DueDate);
            Assert.Equal(employeeId, r.EmployeeId);
        });
    }

    [Fact]
    public async Task HandleAsync_Ignores_Compliance_Rows_With_No_Missing_Documents()
    {
        var handler = MakeHandler(compliance:
        [
            new DocumentComplianceReportItem(Guid.NewGuid(), Guid.NewGuid(), 3, 3, 0, 0, 0, []),
        ]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    // ── summaries ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Computes_Overall_And_PerCategory_Summary_Counts()
    {
        var handler = MakeHandler(
            expiring:
            [
                Doc(kind: ComplianceDocumentKind.Immigration, expiry: Today.AddDays(-2)), // Overdue, ExpiringVisa
                Doc(kind: ComplianceDocumentKind.Certification, expiry: Today.AddDays(10)), // DueSoon, ExpiringCertification
            ],
            compliance: [Missing(Guid.NewGuid(), "Passport")], // Informational, MissingRequiredDocument
            requests: [Request(dueDate: Today.AddDays(-5))]); // Overdue, OutstandingDocumentRequest

        var result = await handler.HandleAsync(Req(), CancellationToken.None);
        var response = result.Value!;

        Assert.Equal(4, response.Summary.Total);
        Assert.Equal(2, response.Summary.Overdue);
        Assert.Equal(1, response.Summary.DueSoon);
        Assert.Equal(1, response.Summary.Informational);

        var visa = response.CategorySummaries.Single(c => c.Category == "ExpiringVisa");
        Assert.Equal(1, visa.Total);
        Assert.Equal(1, visa.Overdue);

        var missing = response.CategorySummaries.Single(c => c.Category == "MissingRequiredDocument");
        Assert.Equal(1, missing.Total);
        Assert.Equal(1, missing.Informational);

        // Every category is represented in the per-category summary even when empty.
        Assert.Equal(6, response.CategorySummaries.Count);
        Assert.Equal(0, response.CategorySummaries.Single(c => c.Category == "ProbationReview").Total);
    }

    [Fact]
    public async Task HandleAsync_Empty_State_Reports_NoActionRequired()
    {
        var handler = MakeHandler();

        var result = await handler.HandleAsync(Req(), CancellationToken.None);
        var response = result.Value!;

        Assert.True(response.NoActionRequired);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(0, response.Summary.Total);
        Assert.False(response.IsTruncated);
        Assert.Equal(6, response.CategorySummaries.Count);
    }

    [Fact]
    public async Task HandleAsync_NoActionRequired_False_When_Any_Item_Present()
    {
        var handler = MakeHandler(requests: [Request()]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        Assert.False(result.Value!.NoActionRequired);
    }

    // ── filters ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Filters_By_Category()
    {
        var handler = MakeHandler(expiring:
        [
            Doc(kind: ComplianceDocumentKind.Immigration),
            Doc(kind: ComplianceDocumentKind.Certification),
        ]);

        var result = await handler.HandleAsync(Req(category: "ExpiringVisa"), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("ExpiringVisa", row.Category);
        Assert.Equal(1, result.Value.Summary.Total);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Severity()
    {
        var handler = MakeHandler(expiring:
        [
            Doc(expiry: Today.AddDays(-1)), // Overdue
            Doc(expiry: Today.AddDays(10)), // DueSoon
        ]);

        var result = await handler.HandleAsync(Req(severity: "Overdue"), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", row.Severity);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Department_Substring_CaseInsensitive()
    {
        var engEmp = Guid.NewGuid();
        var salesEmp = Guid.NewGuid();
        var handler = MakeHandler(
            expiring: [Doc(employeeId: engEmp), Doc(employeeId: salesEmp)],
            directory:
            [
                DirectoryItem(engEmp, department: "Engineering"),
                DirectoryItem(salesEmp, department: "Sales"),
            ]);

        var result = await handler.HandleAsync(Req(department: "eng"), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(engEmp, row.EmployeeId);
        Assert.Equal("Engineering", row.Department);
    }

    [Fact]
    public async Task HandleAsync_DueDate_Range_Filter_Excludes_Null_Due_Rows()
    {
        var handler = MakeHandler(
            expiring: [Doc(expiry: Today.AddDays(5))],
            compliance: [Missing(Guid.NewGuid(), "Passport")], // null due date
            requests: [Request(dueDate: Today.AddDays(60))]);

        var result = await handler.HandleAsync(
            Req(dueStart: Today, dueEnd: Today.AddDays(10)), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(Today.AddDays(5), row.DueDate);
    }

    [Fact]
    public async Task HandleAsync_ManagerId_Restricts_To_Directory_Membership()
    {
        var inLine = Guid.NewGuid();
        var outOfLine = Guid.NewGuid();
        var handler = MakeHandler(
            expiring: [Doc(employeeId: inLine), Doc(employeeId: outOfLine)],
            directory: [DirectoryItem(inLine)]);

        var result = await handler.HandleAsync(Req(managerId: Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(inLine, row.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Without_ManagerId_Keeps_Rows_For_Employees_Absent_From_Directory()
    {
        var handler = MakeHandler(expiring: [Doc()]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Unknown employee", row.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_DueDateEnd_Boundary_Is_Inclusive()
    {
        var handler = MakeHandler(requests:
        [
            Request(dueDate: Today.AddDays(10)),
            Request(dueDate: Today.AddDays(11)),
        ]);

        var result = await handler.HandleAsync(
            Req(dueStart: Today, dueEnd: Today.AddDays(10)), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(Today.AddDays(10), row.DueDate);
    }

    // ── ordering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Orders_Overdue_First_Then_Earliest_Due_Date()
    {
        var handler = MakeHandler(requests:
        [
            Request(dueDate: Today.AddDays(20)),  // DueSoon
            Request(dueDate: Today.AddDays(-1)),  // Overdue, later
            Request(dueDate: Today.AddDays(-10)), // Overdue, earliest
        ]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal(Today.AddDays(-10), items[0].DueDate);
        Assert.Equal(Today.AddDays(-1), items[1].DueDate);
        Assert.Equal(Today.AddDays(20), items[2].DueDate);
    }

    [Fact]
    public async Task HandleAsync_Passes_Route_CompanyId_To_Every_Reader()
    {
        var expiringReader = new FakeExpiringEmployeeDocumentReader();
        var requestReader = new FakeOutstandingDocumentRequestComplianceReader();
        var reviewReader = new FakeProbationReviewComplianceReader();
        var complianceReader = new FakeDocumentComplianceReportReader([]);
        var handler = new GetComplianceCentreHandler(
            expiringReader, complianceReader, requestReader, reviewReader,
            new FakeEmployeeDirectoryReader([]), new FakeClock(FixedUtcNow));

        await handler.HandleAsync(Req(), CancellationToken.None);

        Assert.Equal(CompanyId, expiringReader.LastCompanyId);
        Assert.Equal(CompanyId, requestReader.LastCompanyId);
        Assert.Equal(CompanyId, reviewReader.LastCompanyId);
        Assert.Equal(CompanyId, complianceReader.LastCompanyId);
        Assert.Equal(Today, expiringReader.LastAsOf);
        Assert.Equal(30, expiringReader.LastLookaheadDays);
    }

    [Fact]
    public async Task HandleAsync_Mandatory_Outstanding_Request_Detail_Notes_Mandatory()
    {
        var handler = MakeHandler(requests: [Request(isMandatory: true, typeName: "Right to Work")]);

        var result = await handler.HandleAsync(Req(), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Contains("mandatory", row.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Right to Work", row.Detail);
    }
}
