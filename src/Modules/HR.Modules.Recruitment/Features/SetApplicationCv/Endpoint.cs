using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.SetApplicationCv;

internal sealed class Endpoint(SetApplicationCvHandler handler, ICurrentUser currentUser)
    : Endpoint<SetApplicationCvRequest, SetApplicationCvResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/cv");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        SetApplicationCvRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid performedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, performedBy, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
