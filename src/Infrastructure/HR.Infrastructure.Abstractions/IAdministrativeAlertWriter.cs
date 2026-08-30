namespace HR.Infrastructure.Abstractions;

public interface IAdministrativeAlertWriter
{
    Task RaiseAsync(RaiseAdministrativeAlertCommand command, CancellationToken cancellationToken = default);
}
