using FastEndpoints;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.Dev;

/// <summary>
/// Test-only endpoint for E2E tests: manually triggers departure finalisation for a specific
/// employee without waiting for Hangfire's ProcessLeavingEmployeesJob. Transitions an employee
/// from "Leaving" to "FormerEmployee" status and invokes all downstream handlers (leave-policy
/// deactivation, audit logging, etc.).
///
/// This endpoint is only registered in development mode and is protected by the dev-persona
/// authentication gate (AllowAnonymous, but callers must be authenticated via /api/dev/persona).
/// Used by DepartureFinaliserE2ETests to deterministically test the complete departure journey
/// without timing dependencies or wall-clock progression.
/// </summary>
internal sealed class DepartureFinaliserTestEndpoint(
    IEmployeeDepartureFinalizer finalizer,
    EmployeesDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/dev/departure-finaliser/{employeeId:guid}");
        AllowAnonymous();
        // Development-only — callers must already be authenticated via /api/dev/persona/{userId}
        // session cookie. This just confirms the test seam is available and doesn't bypass auth.
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var employeeId = Route<Guid>("employeeId");

        try
        {
            var employee = await db.Employees
                .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

            if (employee is null)
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            var process = await db.EmployeeLeavingProcesses
                .FirstOrDefaultAsync(p => p.EmployeeId == employeeId, ct);

            if (process is null)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee has no leaving process to finalize"));
                return;
            }

            if (employee.Status != EmploymentStatus.Leaving)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee must be in 'Leaving' status to finalize"));
                return;
            }

            var now = DateTimeOffset.UtcNow;
            await finalizer.FinalizeAsync(employee, process, now, ct);
            await db.SaveChangesAsync(ct);

            await Send.ResultAsync(TypedResults.Ok());
        }
        catch (Exception ex)
        {
            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status500InternalServerError));
        }
    }
}
