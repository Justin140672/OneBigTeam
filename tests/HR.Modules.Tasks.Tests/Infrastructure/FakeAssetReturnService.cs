using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeAssetReturnService : IAssetReturnService
{
    public record Call(Guid CompanyId, Guid AssignmentId, Guid ReturnedBy);

    public List<Call> Calls { get; } = [];

    public Task ReturnAsync(Guid companyId, Guid assignmentId, Guid returnedBy, CancellationToken cancellationToken)
    {
        Calls.Add(new Call(companyId, assignmentId, returnedBy));
        return Task.CompletedTask;
    }
}
