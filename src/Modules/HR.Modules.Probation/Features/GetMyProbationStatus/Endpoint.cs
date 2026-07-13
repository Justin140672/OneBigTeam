using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetMyProbationStatus;

// Self-scoped: resolves the employee purely from the caller's own "sub" claim, matching
// HR.Modules.Employees.Features.GetMyEmployee (Employee.Id == the Supabase auth user id in
// this system) — no role check is required beyond being authenticated, since a caller can only
// ever see their own probation status through this route. This exists because the HR-only
// GetProbationRecordByEmployee endpoint ("probation:manage") 403s for a real employee viewing
// their own profile, which MyProfile.razor's blanket catch{} was silently swallowing.
internal sealed class Endpoint(GetMyProbationStatusHandler handler)
    : EndpointWithoutRequest<GetMyProbationStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/probation-status");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
