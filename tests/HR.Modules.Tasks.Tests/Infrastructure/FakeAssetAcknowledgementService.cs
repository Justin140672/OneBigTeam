using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeAssetAcknowledgementService : IAssetAcknowledgementService
{
    public record Call(Guid CompanyId, Guid AssignmentId, Guid AcknowledgedBy);

    public List<Call> Calls { get; } = [];

    public Task AcknowledgeAsync(Guid companyId, Guid assignmentId, Guid acknowledgedBy, CancellationToken cancellationToken)
    {
        Calls.Add(new Call(companyId, assignmentId, acknowledgedBy));
        return Task.CompletedTask;
    }
}
