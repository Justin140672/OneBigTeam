using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeHrAdministratorDirectory : IHrAdministratorDirectory
{
    public IReadOnlyList<Guid> HrAdministratorEmployeeIds { get; set; } = [];

    public Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(HrAdministratorEmployeeIds);
}
