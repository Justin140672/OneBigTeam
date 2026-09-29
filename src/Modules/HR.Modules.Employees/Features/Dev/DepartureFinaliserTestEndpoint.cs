using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
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
    ICurrentTenant currentTenant,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/dev/departure-finaliser/{companyId:guid}/{employeeId:guid}");
        AllowAnonymous();
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

        // Authentication gate: must be authenticated
        if (!HttpContext.User.Identity?.IsAuthenticated ?? true)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Authorization gate: must hold the HR Administrator role. Roles are not JWT claims (they are
        // resolved from the database), so evaluate the same "role:hr-administrator" policy the rest of
        // the API uses instead of inspecting claims.
        var roleAuth = await authorizationService.AuthorizeAsync(HttpContext.User, "role:hr-administrator");
        if (!roleAuth.Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
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

        // Check the leaving process status: InProgress (ready to finalize) or Completed (idempotent success).
        // Query explicitly for InProgress first, using deterministic ordering (StartedAt DESC, Id DESC)
        // to ensure consistent results when multiple processes exist (e.g., cancelled then in-progress).
        var inProgressProcess = await db.EmployeeLeavingProcesses
            .Where(p => p.EmployeeId == employeeId && p.CompanyId == companyId &&
                        p.Status == LeavingProcessStatus.InProgress)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (inProgressProcess is not null)
        {
            // InProgress process found — ready to finalize
            // (execution phase continues below after this validation phase)
        }
        else
        {
            // No in-progress process. Check idempotency: Completed + Former Employee = idempotent success.
            // This allows E2E tests to call finalization twice after a process has completed.
            var completedProcess = await db.EmployeeLeavingProcesses
                .Where(p => p.EmployeeId == employeeId && p.CompanyId == companyId &&
                            p.Status == LeavingProcessStatus.Completed)
                .OrderByDescending(p => p.StartedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (completedProcess is not null && employee!.Status == EmploymentStatus.FormerEmployee)
            {
                // Idempotent success: process is already completed and employee is former employee
                await Send.ResultAsync(TypedResults.Ok());
                return;
            }

            // No in-progress process and either no completed process or employee not former employee
            if (completedProcess is null)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee has no leaving process to finalize"));
            }
            else
            {
                // Completed process exists but employee is not yet Former Employee (data inconsistency)
                await Send.ResultAsync(TypedResults.BadRequest("Process is completed but employee status is not FormerEmployee"));
            }
            return;
        }

        // Use the in-progress process for finalization
        var process = inProgressProcess;

        // ── Execution Phase ───────────────────────────────────────────────────────────
        // All validation passed. Execute the departure finaliser job, scoped to this company
        // to ensure we don't accidentally finalize employees in other companies.

        try
        {
            // Execute the job scoped to the requested company so it only processes employees
            // in that company with Status == Leaving and LeavingDate <= today. This prevents
            // the cross-company mutation that was happening before.
            await departureFinaliserJob.ExecuteForEmployeeAsync(companyId, employeeId);

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
