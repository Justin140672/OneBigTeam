using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed class Endpoint(WithdrawApplicationHandler handler, ICurrentUser currentUser)
    : Endpoint<WithdrawApplicationRequest, WithdrawApplicationResponse>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        WithdrawApplicationRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid performedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            performedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
