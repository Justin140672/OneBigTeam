using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateCompensationRecord;

internal sealed class Endpoint(CreateCompensationRecordHandler handler)
    : Endpoint<CreateCompensationRecordRequest, CreateCompensationRecordResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/compensation");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateCompensationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/employees/{request.EmployeeId}/compensation/current",
            result.Value));
    }
}
