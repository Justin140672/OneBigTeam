using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetMyOnboardingStatus;

// Self-scoped: resolves the employee purely from the caller's own "sub" claim, matching
// HR.Modules.Employees.Features.GetMyEmployee (Employee.Id == the Supabase auth user id in
// this system) — no role check is required beyond being authenticated, since a caller can only
// ever see their own onboarding status through this route. Backs the new "Onboarding Progress"
// card on MyProfileOverviewTab.razor; deliberately leaner than GetOnboardingOverview (no
// cross-module document/asset/probation reads) since the card only needs plan + task progress.
internal sealed class Endpoint(GetMyOnboardingStatusHandler handler)
    : EndpointWithoutRequest<GetMyOnboardingStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/onboarding-status");
        Policies("role:employee");
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
