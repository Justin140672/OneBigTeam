namespace HR.Infrastructure.Abstractions;

public interface ILegalHoldStatusReader
{
    Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken);
}
