namespace HR.Web.Services;

public class UserService(AppSession session)
{
    public async Task<bool> HasPermissionAsync(Guid permissionId)
    {
        await session.InitialiseAsync();
        return session.PermissionIds.Contains(permissionId);
    }
}
