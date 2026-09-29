using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetUpcomingInterviews;

internal sealed class Endpoint(GetUpcomingInterviewsHandler handler)
    : Endpoint<GetUpcomingInterviewsRequest, GetUpcomingInterviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/interviews/upcoming");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        GetUpcomingInterviewsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
