using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AioKin.Models.InputModel.Auth.User;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Setup;

/// <summary>
/// Hai policy chan brute-force. Cac controller tham chieu chung bang ten qua
/// <c>[EnableRateLimiting("auth")]</c> / <c>[EnableRateLimiting("auth-strict")]</c> —
/// khong dang ky o day thi attribute tro thanh vo hieu trong im lang va endpoint dang
/// nhap khong con gi bao ve.
/// </summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddAioKinRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Tra loi cung khuon OperationResult nhu phan con lai cua API, kem Retry-After.
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    OperationResult.Fail("TooManyRequests", "Ban thao tac qua nhanh. Vui long thu lai sau it phut."),
                    cancellationToken);
            };

            // Dang nhap, OTP, dat lai mat khau — nhung noi ke tan cong do tim.
            options.AddPolicy("auth-strict", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            // Cac endpoint auth con lai (dang ky, gui lai OTP, doi refresh token) — noi hon.
            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
        });

        return services;
    }

    /// <summary>
    /// Phan vung theo user da dang nhap, chua dang nhap thi theo IP. IP chi dung khi
    /// UseForwardedHeaders da khoi phuc X-Forwarded-For: dung sau reverse proxy ma khong
    /// bat, moi request se roi vao cung mot phan vung la IP cua proxy va mot nguoi dung
    /// se lam het quota cua tat ca.
    /// </summary>
    private static string ResolveClientKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return "u:" + userId;

        return "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
