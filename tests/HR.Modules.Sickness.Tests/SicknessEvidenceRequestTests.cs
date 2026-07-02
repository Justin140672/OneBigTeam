using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Tests;

public class SicknessEvidenceRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SicknessRecordId = Guid.NewGuid();
    private static readonly Guid RequestedBy = Guid.NewGuid();
    private static readonly DateOnly DueDate = new(2026, 7, 9);

    private static SicknessEvidenceRequest CreateDefault(string? notes = null) =>
        SicknessEvidenceRequest.Create(Id, CompanyId, SicknessRecordId, RequestedBy, DueDate, notes, Now);

    [Fact]
    public void Create_SetsAllFields()
    {
        var request = CreateDefault("Please submit your fit note.");

        Assert.Equal(Id, request.Id);
        Assert.Equal(CompanyId, request.CompanyId);
        Assert.Equal(SicknessRecordId, request.SicknessRecordId);
        Assert.Equal(RequestedBy, request.RequestedBy);
        Assert.Equal(DueDate, request.DueDate);
        Assert.Equal("Please submit your fit note.", request.Notes);
        Assert.Equal(SicknessEvidenceRequestStatus.Pending, request.Status);
        Assert.Equal(Now, request.RequestedAt);
        Assert.Equal(Now, request.CreatedAt);
        Assert.Equal(Now, request.UpdatedAt);
        Assert.Null(request.FulfilledAt);
    }

    [Fact]
    public void Create_WithNullNotes_SetsNullNotes()
    {
        var request = CreateDefault(null);

        Assert.Null(request.Notes);
    }

    [Fact]
    public void Create_SetsStatusToPending()
    {
        var request = CreateDefault();

        Assert.Equal(SicknessEvidenceRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Fulfil_SetsStatusFulfilledAndFulfilledAt()
    {
        var request = CreateDefault();
        var fulfilledAt = Now.AddDays(3);

        request.Fulfil(fulfilledAt);

        Assert.Equal(SicknessEvidenceRequestStatus.Fulfilled, request.Status);
        Assert.Equal(fulfilledAt, request.FulfilledAt);
        Assert.Equal(fulfilledAt, request.UpdatedAt);
    }

    [Fact]
    public void Cancel_SetsStatusCancelled()
    {
        var request = CreateDefault();
        var cancelledAt = Now.AddDays(1);

        request.Cancel(cancelledAt);

        Assert.Equal(SicknessEvidenceRequestStatus.Cancelled, request.Status);
        Assert.Equal(cancelledAt, request.UpdatedAt);
        Assert.Null(request.FulfilledAt);
    }

    [Fact]
    public void MarkOverdue_SetsStatusOverdue()
    {
        var request = CreateDefault();
        var overdueAt = Now.AddDays(7);

        request.MarkOverdue(overdueAt);

        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, request.Status);
        Assert.Equal(overdueAt, request.UpdatedAt);
        Assert.Null(request.FulfilledAt);
    }

    [Fact]
    public void Fulfil_DoesNotClearNotes()
    {
        var request = CreateDefault("some notes");
        request.Fulfil(Now.AddDays(2));

        Assert.Equal("some notes", request.Notes);
    }
}
