namespace HR.Web.Services;

public class UserService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    private IReadOnlySet<Guid>? _cachedPermissions;

    public async Task<IReadOnlySet<Guid>> GetPermissionsAsync()
    {
        if (_cachedPermissions is not null)
            return _cachedPermissions;

        try
        {
            var response = await Http.GetFromJsonAsync<PermissionsResponse>("api/users/me/permissions");
            _cachedPermissions = response?.PermissionIds.ToHashSet() ?? [];
        }
        catch
        {
            // If the permissions endpoint is unreachable or returns an error,
            // treat as no permissions. The API enforces authorization server-side.
            _cachedPermissions = new HashSet<Guid>();
        }

        return _cachedPermissions;
    }

    public async Task<bool> HasPermissionAsync(Guid permissionId)
    {
        var permissions = await GetPermissionsAsync();
        return permissions.Contains(permissionId);
    }

    private sealed record PermissionsResponse(List<Guid> PermissionIds);
}
