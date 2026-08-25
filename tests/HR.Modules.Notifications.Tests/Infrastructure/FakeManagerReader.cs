using HR.Modules.Employees.Contracts;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeManagerReader : IManagerReader
{
    public Guid? ManagerId { get; set; }

    public Task<Guid?> GetManagerIdAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(ManagerId);
}
