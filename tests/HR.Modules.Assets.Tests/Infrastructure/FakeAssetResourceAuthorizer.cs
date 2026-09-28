using HR.Modules.Assets.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Assets.Tests.Infrastructure;

/// <summary>
/// Ticket 3: Test implementation of AssetResourceAuthorizer that bypasses actual authorization
/// logic and just allows/denies access based on constructor flag.
/// </summary>
internal sealed class FakeAssetResourceAuthorizer
{
    private readonly AssetResourceAuthorizer _realAuthorizer;
    private readonly bool _allowAccess;

    public FakeAssetResourceAuthorizer(bool allowAccess)
    {
        _allowAccess = allowAccess;
        // Create the real authorizer with test dependencies
        _realAuthorizer = new AssetResourceAuthorizer(
            new FakeAuthorizationService(),
            new FakeDirectReportsReader());
    }

    public Task<bool> CanViewAssetAsync(
        Guid companyId, Guid callerEmployeeId, Guid assignedEmployeeId, CancellationToken cancellationToken)
    {
        // In unit tests, bypass real authorization logic and just return the configured value
        return Task.FromResult(_allowAccess);
    }
}
