using HR.Modules.Identity.Authorization;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="ITargetUserCompanyGuard"/> (IAM-01). Defaults to
/// <c>isMember: true</c> so existing handler tests that don't care about the guard keep exercising
/// their original behaviour; pass <c>isMember: false</c> to assert the guard's NotFound short-circuit.
/// </summary>
internal sealed class FakeTargetUserCompanyGuard(bool isMember = true) : ITargetUserCompanyGuard
{
    public (Guid CompanyId, Guid UserId)? LastCall { get; private set; }

    public Task<bool> IsMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        LastCall = (companyId, userId);
        return Task.FromResult(isMember);
    }
}
