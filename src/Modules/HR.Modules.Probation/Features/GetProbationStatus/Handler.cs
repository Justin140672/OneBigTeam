using HR.Infrastructure.Abstractions;

namespace HR.Modules.Probation.Features.GetProbationStatus;

internal sealed class GetProbationStatusHandler(IProbationStatusReader statusReader)
{
    public async Task<GetProbationStatusResponse> HandleAsync(
        GetProbationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = await statusReader.GetStatusAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        return status is null
            ? new GetProbationStatusResponse(false, null)
            : new GetProbationStatusResponse(true, status.Status);
    }
}
