using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Logging;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 23 (P2) acceptance criterion: "one correlation ID across request logs, integration-event
/// dispatch, durable operation records". Calls the real CreateEmployee endpoint with an explicit
/// <see cref="CorrelationIdMiddleware.HeaderName"/> header, then reads
/// <c>employees.audit_outbox</c> directly via <see cref="EmployeesDbContext"/> and asserts the
/// staged <see cref="EmployeeCreatedIntegrationEvent"/> outbox row's CorrelationId column equals the
/// GUID supplied on the request — proving the id survives from the HTTP request, through the
/// handler, into the durable outbox record (dispatched inline synchronously, per
/// <c>CreateEmployeeHandler</c>, so the row is visible with its CorrelationId intact immediately
/// after the response returns).
/// </summary>
[Collection("Integration")]
public class CorrelationIdPropagatesToOutboxEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("aaaaaaaa-1111-0000-0000-000000000001");

    public CorrelationIdPropagatesToOutboxEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Supplied_CorrelationId_Header_Is_Persisted_On_The_Staged_EmployeeCreatedIntegrationEvent_Outbox_Row()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);

        var suppliedCorrelationId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, suppliedCorrelationId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Correlation", "Test", $"correlation.test.{Guid.NewGuid():N}@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Equal(
            suppliedCorrelationId.ToString(),
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single(),
            ignoreCase: true);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var outboxEntry = await db.AuditOutboxEntries
            .Where(e => e.CompanyId == companyId && e.EventTypeName.Contains(nameof(EmployeeCreatedIntegrationEvent)))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(outboxEntry);
        Assert.Equal(suppliedCorrelationId, outboxEntry!.CorrelationId);
    }

    private sealed record EmployeePayload(Guid Id, Guid CompanyId);
}
