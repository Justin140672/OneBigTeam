using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetMyOnboardingStatus;

// Self-scoped: resolves the employee via ICurrentUser.UserId (this app's resolved Employee/UserId,
// NOT the raw Supabase "sub" claim — see GetMyEmployee/Endpoint.cs) — no role check is required
// beyond being authenticated, since a caller can only ever see their own onboarding status through
// this route. Backs the new "Onboarding Progress" card on MyProfileOverviewTab.razor; deliberately
// leaner than GetOnboardingOverview (no cross-module document/asset/probation reads) since the
// card only needs plan + task progress.
internal sealed class Endpoint(GetMyOnboardingStatusHandler handler, ICurrentUser currentUser)
    : EndpointWithoutRequest<GetMyOnboardingStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/onboarding-status");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
