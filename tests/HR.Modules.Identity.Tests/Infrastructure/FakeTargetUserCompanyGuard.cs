using HR.Modules.Identity.Authorization;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeTargetUserCompanyGuard(bool isMember = true) : ITargetUserCompanyGuard
{
    public (Guid CompanyId, Guid UserId)? LastCall { get; private set; }

    public Task<bool> IsMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        LastCall = (companyId, userId);
        return Task.FromResult(isMember);
    }
}
