using System.IdentityModel.Tokens.Jwt;
using AioKin.Common;
using AioKin.Services.Common.Cache;

namespace AioKin.Middleware;

/// <summary>
/// Chan access token da bi thu hoi (logout). JWT tu no khong the bi huy truoc han, nen
/// logout ghi JTI vao Redis va middleware nay tra cuu tren moi request.
///
/// Thu tu trong pipeline phai la:
///   UseAuthentication() → UseJwtBlacklist() → UseAuthorization()
/// Dat sau UseAuthorization thi endpoint da chay xong moi kiem tra — qua muon.
/// </summary>
public class JwtBlacklistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtBlacklistMiddleware> _logger;

    public JwtBlacklistMiddleware(RequestDelegate next, ILogger<JwtBlacklistMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRedisService redis)
    {
        // Doc JTI tu principal da duoc JwtBearer validate, thay vi tu parse lai header:
        // token chua qua validation thi khong dang de ton mot vong tra cuu Redis.
        var jti = context.User.GetJti();

        if (!string.IsNullOrEmpty(jti) && await redis.ExistsAsync(RedisKeys.JwtBlacklist(jti)))
        {
            _logger.LogWarning("Blacklisted token attempt. JTI={Jti}, IP={IP}",
                jti, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"success":false,"errorCode":"TokenRevoked","message":"Token da bi thu hoi. Vui long dang nhap lai."}""");
            return;
        }

        await _next(context);
    }

    /// <summary>Doc JTI ma khong validate chu ky — dung cho truong hop can JTI truoc khi authenticate.</summary>
    internal static string? ExtractJtiUnvalidated(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.CanReadToken(token) ? handler.ReadJwtToken(token).Id : null;
        }
        catch
        {
            return null;
        }
    }
}

public static class JwtBlacklistMiddlewareExtensions
{
    public static IApplicationBuilder UseJwtBlacklist(this IApplicationBuilder app)
        => app.UseMiddleware<JwtBlacklistMiddleware>();
}
