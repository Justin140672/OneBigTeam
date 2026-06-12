using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed class Endpoint(SetEmployeeWorkingPatternHandler handler)
    : Endpoint<SetEmployeeWorkingPatternRequest, SetEmployeeWorkingPatternResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/working-pattern");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        SetEmployeeWorkingPatternRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
