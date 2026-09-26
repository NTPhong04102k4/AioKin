using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AioKin.Tests.Infrastructure;

/// <summary>Dung khi test can mot HttpContext mang danh tinh nguoi dung ma khong di qua HTTP that.</summary>
public static class TestHttpContext
{
    public static HttpContext ForUser(Guid userUuid)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userUuid.ToString())], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
