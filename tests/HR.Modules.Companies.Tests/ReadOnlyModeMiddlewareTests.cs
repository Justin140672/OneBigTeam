using System.Security.Claims;
using System.Text.Json;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Tests;

public class ReadOnlyModeMiddlewareTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task InvokeAsync_Always_Calls_Next_For_Read_Methods_Regardless_Of_ReadOnly_Status(string method)
    {
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0)
        };
        var context = BuildAuthenticatedContext(method, "/api/companies/123", Guid.NewGuid());

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.For(Guid.NewGuid().ToString()));

        Assert.True(nextCalled);
        Assert.False(reader.WasCalled);
    }

    [Theory]
    [InlineData("/api/companies/checkout-session")]
    [InlineData("/api/companies/stripe-webhook")]
    [InlineData("/api/companies/subscription/cancel")]
    [InlineData("/api/companies/subscription/resume")]
    [InlineData("/api/companies/subscription/billing-portal")]
    [InlineData("/api/signup")]
    [InlineData("/api/dev/personas")]
    public async Task InvokeAsync_Always_Calls_Next_For_AllowListed_Paths_Regardless_Of_ReadOnly_Status(string path)
    {
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0)
        };
        var context = BuildAuthenticatedContext("POST", path, Guid.NewGuid());

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.For(Guid.NewGuid().ToString()));

        Assert.True(nextCalled);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task InvokeAsync_Calls_Next_For_Unauthenticated_Mutation_Request()
    {
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0)
        };
        var context = new DefaultHttpContext
        {
            Request = { Method = "POST", Path = "/api/companies/some-mutation" }
        };
        context.Response.Body = new MemoryStream();
        // No authenticated user set — IsAuthenticated defaults to false.

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.For(Guid.NewGuid().ToString()));

        Assert.True(nextCalled);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task InvokeAsync_Calls_Next_When_Authenticated_But_No_Tenant_Resolved()
    {
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0)
        };
        var context = BuildAuthenticatedContext("POST", "/api/companies/some-mutation", companyId: null);

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.None);

        Assert.True(nextCalled);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task InvokeAsync_Calls_Next_When_Tenant_Resolved_And_Not_ReadOnly()
    {
        var companyId = Guid.NewGuid();
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.Active, IsReadOnly: false, TrialDaysRemaining: 0)
        };
        var context = BuildAuthenticatedContext("POST", "/api/companies/some-mutation", companyId);

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.For(companyId.ToString()));

        Assert.True(nextCalled);
        Assert.True(reader.WasCalled);
        Assert.Equal(companyId, reader.LastCompanyId);
    }

    [Fact]
    public async Task InvokeAsync_Returns_403_With_Structured_Body_And_Does_Not_Call_Next_When_ReadOnly_On_NonAllowListed_Mutation()
    {
        var companyId = Guid.NewGuid();
        var reader = new FakeSubscriptionStatusReader
        {
            SnapshotToReturn = new SubscriptionStatusSnapshot(SubscriptionStatus.TrialExpired, IsReadOnly: true, TrialDaysRemaining: 0)
        };
        var context = BuildAuthenticatedContext("POST", "/api/companies/some-mutation", companyId);
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, reader, FakeCurrentTenant.For(companyId.ToString()));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("subscription_read_only", doc.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    private static DefaultHttpContext BuildAuthenticatedContext(string method, string path, Guid? companyId = null)
    {
        _ = companyId; // tenant is supplied separately via FakeCurrentTenant at the call site.

        var context = new DefaultHttpContext
        {
            Request = { Method = method, Path = path }
        };
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], authenticationType: "test"));
        return context;
    }
}
