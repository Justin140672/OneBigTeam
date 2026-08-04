using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.GetSupportDashboard;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Tests;

public class GetSupportDashboardHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SupportRequest CreateRequest(
        Guid companyId,
        SupportRequestType type,
        string title,
        SupportRequestStatus status = SupportRequestStatus.Submitted,
        DateTimeOffset? createdAt = null)
    {
        var entity = SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            type, title, "Description", SupportRequestPriority.Low,
            $"SUP-{Guid.NewGuid():N}"[..12], null, null, null, false, null, null, createdAt ?? Now);

        if (status != SupportRequestStatus.Submitted)
            entity.ChangeStatus(status, createdAt ?? Now);

        return entity;
    }

    [Theory]
    [InlineData((int)SupportRequestStatus.Submitted, true)]
    [InlineData((int)SupportRequestStatus.UnderReview, true)]
    [InlineData((int)SupportRequestStatus.Planned, true)]
    [InlineData((int)SupportRequestStatus.WaitingForCustomer, true)]
    [InlineData((int)SupportRequestStatus.Resolved, false)]
    [InlineData((int)SupportRequestStatus.Closed, false)]
    public async Task HandleAsync_OpenCount_Counts_Correctly_By_Status(int statusValue, bool countedAsOpen)
    {
        var status = (SupportRequestStatus)statusValue;
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q", status));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(countedAsOpen ? 1 : 0, result.OpenRequestsCount);
    }

    [Fact]
    public async Task HandleAsync_OpenCount_Excludes_Resolved_And_Closed_But_Includes_Others()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q1", SupportRequestStatus.Submitted));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q2", SupportRequestStatus.UnderReview));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q3", SupportRequestStatus.Resolved));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q4", SupportRequestStatus.Closed));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, result.OpenRequestsCount);
    }

    [Fact]
    public async Task HandleAsync_AverageStaffResponseTime_Is_Null_When_No_Staff_Responses_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q1"));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Null(result.AverageStaffResponseTimeHours);
    }

    [Fact]
    public async Task HandleAsync_Calculates_Average_Staff_Response_Time_In_Hours()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var request1 = CreateRequest(companyId, SupportRequestType.AskQuestion, "Q1", createdAt: Now);
        var request2 = CreateRequest(companyId, SupportRequestType.AskQuestion, "Q2", createdAt: Now);
        db.SupportRequests.Add(request1);
        db.SupportRequests.Add(request2);
        await db.SaveChangesAsync();

        // First staff response after 2 hours for request1, 4 hours for request2 -> average 3 hours.
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request1.Id, companyId, Guid.NewGuid(), true, "Reply 1", Now.AddHours(2)));
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request2.Id, companyId, Guid.NewGuid(), true, "Reply 2", Now.AddHours(4)));
        // A later staff response on request1 must not affect the FIRST-response average.
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request1.Id, companyId, Guid.NewGuid(), true, "Reply 1b", Now.AddHours(10)));
        // A customer response should never count as a staff response time sample.
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request1.Id, companyId, Guid.NewGuid(), false, "Customer reply", Now.AddHours(1)));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.NotNull(result.AverageStaffResponseTimeHours);
        Assert.Equal(3.0, result.AverageStaffResponseTimeHours!.Value, precision: 3);
    }

    [Fact]
    public async Task HandleAsync_Breaks_Down_Requests_By_Type()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.ReportProblem, "P1"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.ReportProblem, "P2"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.RequestFeature, "F1"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.AskQuestion, "Q1"));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, result.RequestsByType.Single(t => t.Type == nameof(SupportRequestType.ReportProblem)).Count);
        Assert.Equal(1, result.RequestsByType.Single(t => t.Type == nameof(SupportRequestType.RequestFeature)).Count);
        Assert.Equal(1, result.RequestsByType.Single(t => t.Type == nameof(SupportRequestType.AskQuestion)).Count);
    }

    [Fact]
    public async Task HandleAsync_Ranks_Top_Requested_Features_And_Reported_Problems_By_Count_Descending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.RequestFeature, "Bulk export"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.RequestFeature, "Bulk export"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.RequestFeature, "Dark mode"));
        db.SupportRequests.Add(CreateRequest(companyId, SupportRequestType.ReportProblem, "Login broken"));
        await db.SaveChangesAsync();

        var handler = new GetSupportDashboardHandler(db);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal("Bulk export", result.TopRequestedFeatures[0].Title);
        Assert.Equal(2, result.TopRequestedFeatures[0].Count);
        Assert.Contains(result.TopReportedProblems, p => p.Title == "Login broken");
        Assert.DoesNotContain(result.TopRequestedFeatures, p => p.Title == "Login broken");
    }
}
