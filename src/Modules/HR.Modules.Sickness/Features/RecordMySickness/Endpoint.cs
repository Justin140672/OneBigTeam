using FastEndpoints;
using HR.Modules.Sickness.Features.RecordSickness;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class Endpoint(RecordMySicknessHandler handler)
    : Endpoint<RecordMySicknessRequest, RecordSicknessResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/my");
        Policies("authenticated");
    }

    public override async Task HandleAsync(RecordMySicknessRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var authenticatedEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (authenticatedEmployeeId != request.EmployeeId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/employees/{request.EmployeeId}/sickness-records/{result.Value!.Id}",
            result.Value));
    }
}
