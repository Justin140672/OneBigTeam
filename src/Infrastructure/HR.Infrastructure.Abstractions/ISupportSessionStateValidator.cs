namespace HR.Infrastructure.Abstractions;

public interface ISupportSessionStateValidator
{
    Task<bool> IsActiveAsync(
        Guid supportSessionId,
        Guid companyId,
        Guid adminUserId,
        string? adminEmail,
        CancellationToken cancellationToken);
}
