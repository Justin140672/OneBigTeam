namespace HR.SharedKernel.Contracts;

public interface IAssetReturnService
{
    Task ReturnAsync(Guid companyId, Guid assignmentId, Guid returnedBy, CancellationToken cancellationToken);
}
