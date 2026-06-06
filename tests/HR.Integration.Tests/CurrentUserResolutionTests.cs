using System.Security.Claims;
using HR.Modules.Identity;
using Microsoft.AspNetCore.Http;

namespace HR.Integration.Tests;

public class CurrentUserResolutionTests
{
    [Fact]
    public void CurrentUser_Uses_Resolved_Profile_From_HttpContext_Items()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var expectedUserId = Guid.NewGuid();
        accessor.HttpContext.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey] =
            new ResolvedCurrentUser(expectedUserId, "resolved@company.com", "tenant-1", true);

        var currentUser = new HttpContextCurrentUser(accessor);

        Assert.Equal(expectedUserId, currentUser.UserId);
        Assert.Equal("resolved@company.com", currentUser.Email);
        Assert.Equal("tenant-1", currentUser.TenantId);
        Assert.True(currentUser.IsAuthenticated);
    }

    [Fact]
    public void CurrentUser_Falls_Back_To_Supabase_Claims_When_Not_Resolved()
    {
        var sub = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(CurrentUserClaims.SupabaseUserId, sub.ToString()),
            new Claim(CurrentUserClaims.Email, "supabase@company.com"),
            new Claim(CurrentUserClaims.TenantId, "tenant-2")
        };

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"))
            }
        };

        var currentUser = new HttpContextCurrentUser(accessor);

        Assert.Equal(sub, currentUser.UserId);
        Assert.Equal("supabase@company.com", currentUser.Email);
        Assert.Equal("tenant-2", currentUser.TenantId);
        Assert.True(currentUser.IsAuthenticated);
    }

    [Fact]
    public void CurrentUser_Returns_Anonymous_When_No_HttpContext_User()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var currentUser = new HttpContextCurrentUser(accessor);

        Assert.Null(currentUser.UserId);
        Assert.Null(currentUser.Email);
        Assert.Null(currentUser.TenantId);
        Assert.False(currentUser.IsAuthenticated);
    }
}