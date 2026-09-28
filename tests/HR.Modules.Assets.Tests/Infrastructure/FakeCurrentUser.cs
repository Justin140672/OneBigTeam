using HR.SharedKernel;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId, Guid companyId)
    {
        UserId = userId;
        CompanyId = companyId;
        TenantId = companyId.ToString();
    }

    public Guid? UserId { get; }
    public Guid CompanyId { get; }
    public string? TenantId { get; }
    public bool IsAuthenticated => UserId.HasValue;
    public string? Email => "test@example.com";
}
