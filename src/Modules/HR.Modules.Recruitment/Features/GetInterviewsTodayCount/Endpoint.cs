using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetInterviewsTodayCount;

internal sealed class Endpoint(GetInterviewsTodayCountHandler handler)
    : Endpoint<GetInterviewsTodayCountRequest, GetInterviewsTodayCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/interviews/today-count");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetInterviewsTodayCountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
