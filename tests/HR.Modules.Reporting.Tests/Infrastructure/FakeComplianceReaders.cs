using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>Hand-rolled fakes for the ADM-02 Compliance Centre contract readers.</summary>
internal sealed class FakeExpiringEmployeeDocumentReader(
    IReadOnlyList<ExpiringEmployeeDocumentItem>? items = null) : IExpiringEmployeeDocumentReader
{
    private readonly IReadOnlyList<ExpiringEmployeeDocumentItem> _items = items ?? [];

    public Guid? LastCompanyId { get; private set; }
    public DateOnly? LastAsOf { get; private set; }
    public int? LastLookaheadDays { get; private set; }

    public Task<IReadOnlyList<ExpiringEmployeeDocumentItem>> GetExpiringEmployeeDocumentsAsync(
        Guid companyId, DateOnly asOf, int lookaheadDays, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastAsOf = asOf;
        LastLookaheadDays = lookaheadDays;
        return Task.FromResult(_items);
    }
}

internal sealed class FakeOutstandingDocumentRequestComplianceReader(
    IReadOnlyList<OutstandingDocumentRequestComplianceItem>? items = null)
    : IOutstandingDocumentRequestComplianceReader
{
    private readonly IReadOnlyList<OutstandingDocumentRequestComplianceItem> _items = items ?? [];

    public Guid? LastCompanyId { get; private set; }

    public Task<IReadOnlyList<OutstandingDocumentRequestComplianceItem>> GetOutstandingDocumentRequestsAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult(_items);
    }
}

internal sealed class FakeProbationReviewComplianceReader(
    IReadOnlyList<ProbationReviewComplianceItem>? items = null) : IProbationReviewComplianceReader
{
    private readonly IReadOnlyList<ProbationReviewComplianceItem> _items = items ?? [];

    public Guid? LastCompanyId { get; private set; }

    public Task<IReadOnlyList<ProbationReviewComplianceItem>> GetPendingProbationReviewsAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult(_items);
    }
}
