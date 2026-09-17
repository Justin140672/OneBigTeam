using System.Net;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class DashboardServiceTests
{
    private static readonly DashboardSummaryModel SampleSummary = new(
        Categories: [],
        TotalActionableCount: 0,
        AllRequiredLoaded: true,
        HasPartialFailure: false,
        AsOfDate: new DateOnly(2026, 9, 16));

    public static IEnumerable<object[]> BothMethods()
    {
        yield return new object[] { (Func<DashboardService, Guid, Task<DashboardSummaryModel>>)((s, id) => s.GetHrSummaryOrThrowAsync(id)) };
        yield return new object[] { (Func<DashboardService, Guid, Task<DashboardSummaryModel>>)((s, id) => s.GetManagerSummaryOrThrowAsync(id)) };
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Returns_Deserialized_Summary_When_Api_Returns_Ok(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, SampleSummary));
        var service = new DashboardService(factory);

        var result = await call(service, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Empty(result.Categories);
        Assert.True(result.AllRequiredLoaded);
        Assert.False(result.HasPartialFailure);
        Assert.Equal(new DateOnly(2026, 9, 16), result.AsOfDate);
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Throws_When_Api_Returns_Unauthorized(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new DashboardService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => call(service, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Throws_When_Api_Returns_Forbidden(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new DashboardService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => call(service, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Throws_When_Api_Returns_InternalServerError(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, null));
        var service = new DashboardService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => call(service, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Throws_When_Success_Body_Is_Malformed(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new DashboardService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => call(service, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(BothMethods))]
    public async Task Throws_When_HttpClient_Throws_Network_Failure(Func<DashboardService, Guid, Task<DashboardSummaryModel>> call)
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new DashboardService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => call(service, Guid.NewGuid()));
    }
}
