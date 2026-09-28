using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Features.Dev;

/// <summary>
/// Test-only endpoint for E2E tests: manually triggers the ProcessLeavingEmployeesJob to process
/// all employees whose leaving date has passed (due for finalisation). This endpoint replaces the
/// previous single-employee finalization seam with a more realistic invocation of the daily job,
/// exercising the actual departure finalization workflow used in production.
///
/// The endpoint accepts a company ID and employee ID in the route for authentication and validation
/// purposes (verifying the caller can access that company and the employee exists), but the job
/// itself processes all employees in the company with Status == Leaving and LeavingDate <= today.
///
/// This endpoint is only accessible when:
/// - Environment is Development
/// - E2E_TESTING environment variable is "true"
/// - Caller is authenticated as an HR Administrator
/// - Caller belongs to the specified company
///
/// Used by DepartureFinaliserE2ETests to deterministically test the complete departure journey,
/// including multi-employee batch processing and job idempotency, without wall-clock dependencies.
/// </summary>
internal sealed class DepartureFinaliserTestEndpoint(
    ProcessLeavingEmployeesJob departureFinaliserJob,
    EmployeesDbContext db,
    ICurrentTenant currentTenant) : EndpointWithoutRequest
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

        // ── Validation Phase ──────────────────────────────────────────────────────────
        // Separate validation from execution so we can report whether the target is already
        // complete (idempotency: second calls for finished targets return success, not error).

        // Verify current tenant matches the route company
        if (!Guid.TryParse(currentTenant.TenantId, out var userCompanyId) || userCompanyId != companyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // Validate that the employee exists and belongs to this company
        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct);

        if (employee is null)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        // Check the leaving process status: InProgress (ready to finalize) or Completed (idempotent success)
        var process = await db.EmployeeLeavingProcesses
            .FirstOrDefaultAsync(
                p => p.EmployeeId == employeeId && p.CompanyId == companyId,
                ct);

        if (process is null)
        {
            await Send.ResultAsync(TypedResults.BadRequest("Employee has no leaving process to finalize"));
            return;
        }

        // Idempotency: if the process is already Completed, return success (not an error).
        // This allows E2E tests to call finalization twice without assertion failure.
        if (process.Status == LeavingProcessStatus.Completed)
        {
            await Send.ResultAsync(TypedResults.Ok());
            return;
        }

        // Only InProgress processes can be finalized
        if (process.Status != LeavingProcessStatus.InProgress)
        {
            await Send.ResultAsync(TypedResults.BadRequest($"Only InProgress processes can be finalized; current status is {process.Status}"));
            return;
        }

        // ── Execution Phase ───────────────────────────────────────────────────────────
        // All validation passed. Execute the departure finaliser job, scoped to this company
        // to ensure we don't accidentally finalize employees in other companies.

        try
        {
            // Execute the job scoped to the requested company so it only processes employees
            // in that company with Status == Leaving and LeavingDate <= today. This prevents
            // the cross-company mutation that was happening before.
            await departureFinaliserJob.ExecuteAsync(companyId);

            await Send.ResultAsync(TypedResults.Ok());
        }
        catch (Exception ex)
        {
            // Log the exception before returning 500 so failures are visible in logs
            var logger = HttpContext.RequestServices.GetService<ILogger<DepartureFinaliserTestEndpoint>>();
            if (logger is not null)
            {
                logger.LogError(ex, "DepartureFinaliserTestEndpoint failed for employee {EmployeeId} in company {CompanyId}", employeeId, companyId);
            }

            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status500InternalServerError));
        }
    }
}
