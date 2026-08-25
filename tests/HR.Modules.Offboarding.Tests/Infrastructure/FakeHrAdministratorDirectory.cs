using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeHrAdministratorDirectory(IReadOnlyList<Guid>? hrAdministratorEmployeeIds = null)
    : IHrAdministratorDirectory
{
    private readonly IReadOnlyList<Guid> _hrAdministratorEmployeeIds = hrAdministratorEmployeeIds ?? [];

    public Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(_hrAdministratorEmployeeIds);
}
