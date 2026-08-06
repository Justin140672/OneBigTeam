using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetMyProbationStatus;

// Self-scoped: resolves the employee via ICurrentUser.UserId (this app's resolved Employee/UserId,
// NOT the raw Supabase "sub" claim — see GetMyEmployee/Endpoint.cs) — no role check is required
// beyond being authenticated, since a caller can only ever see their own probation status through
// this route. This exists because the HR-only GetProbationRecordByEmployee endpoint
// ("probation:manage") 403s for a real employee viewing their own profile, which MyProfile.razor's
// blanket catch{} was silently swallowing.
internal sealed class Endpoint(GetMyProbationStatusHandler handler, ICurrentUser currentUser)
    : EndpointWithoutRequest<GetMyProbationStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/probation-status");
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
