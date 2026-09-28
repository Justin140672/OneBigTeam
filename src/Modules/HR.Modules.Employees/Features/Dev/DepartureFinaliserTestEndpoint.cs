using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.Dev;

/// <summary>
/// Test-only endpoint for E2E tests: manually triggers departure finalisation for a specific
/// employee without waiting for Hangfire's ProcessLeavingEmployeesJob. Transitions an employee
/// from "Leaving" to "FormerEmployee" status and invokes all downstream handlers (leave-policy
/// deactivation, audit logging, etc.).
///
/// This endpoint is only accessible when:
/// - Environment is Development
/// - E2E_TESTING environment variable is "true"
/// - Caller is authenticated as an HR Administrator
/// - Caller belongs to the specified company
///
/// Used by DepartureFinaliserE2ETests to deterministically test the complete departure journey
/// without timing dependencies or wall-clock progression.
/// </summary>
internal sealed class DepartureFinaliserTestEndpoint(
    IEmployeeDepartureFinalizer finalizer,
    EmployeesDbContext db,
    ICurrentTenant currentTenant,
    IClock clock) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/dev/departure-finaliser/{companyId:guid}/{employeeId:guid}");
        Policies("role:hr-administrator");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Environment gate: only accessible in Development with E2E_TESTING=true
        var isE2ETesting = string.Equals(
            Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

        if (!isE2ETesting)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        var companyId = Route<Guid>("companyId");
        var employeeId = Route<Guid>("employeeId");

        try
        {
            // Verify current tenant matches the route company
            if (!Guid.TryParse(currentTenant.TenantId, out var userCompanyId) || userCompanyId != companyId)
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }

            var employee = await db.Employees
                .FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct);

            if (employee is null)
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            // Select only in-progress processes, not historical/cancelled ones
            var process = await db.EmployeeLeavingProcesses
                .FirstOrDefaultAsync(
                    p => p.EmployeeId == employeeId && p.CompanyId == companyId && p.Status == LeavingProcessStatus.InProgress,
                    ct);

            if (process is null)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee has no in-progress leaving process to finalize"));
                return;
            }

            if (employee.Status != EmploymentStatus.Leaving)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee must be in 'Leaving' status to finalize"));
                return;
            }

            var now = clock.UtcNow;
            await finalizer.FinalizeAsync(employee, process, now, ct);
            await db.SaveChangesAsync(ct);

            await Send.ResultAsync(TypedResults.Ok());
        }
        catch (Exception)
        {
            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status500InternalServerError));
        }
    }
}
