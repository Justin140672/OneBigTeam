using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListExternalRecruiters;

internal sealed class Endpoint(ListExternalRecruitersHandler handler)
    : Endpoint<ListExternalRecruitersRequest, ListExternalRecruitersResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/external-recruiters");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        ListExternalRecruitersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
