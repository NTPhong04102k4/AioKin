using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Middleware;

/// <summary>
/// Bien moi ngoai le chua bat thanh ProblemDetails kem correlation id, de log phia server
/// va bao loi phia client noi duoc voi nhau ma khong lo chi tiet noi bo ra ngoai.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = EnsureCorrelationId(context);

        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteProblemAsync(context, correlationId, StatusCodes.Status403Forbidden, "Forbidden", ex.Message, ex);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, correlationId, StatusCodes.Status500InternalServerError,
                "Internal Server Error", "Da xay ra loi he thong.", ex);
        }
    }

    private static string EnsureCorrelationId(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = context.TraceIdentifier;

        context.Response.Headers[CorrelationIdHeader] = correlationId;
        return correlationId;
    }

    private async Task WriteProblemAsync(
        HttpContext context, string correlationId, int statusCode, string title, string detail, Exception ex)
    {
        // Response da gui di mot phan thi khong the ghi de — nem lai de host xu ly,
        // ghi de nua se tao ra body hong.
        if (context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception after response started. CorrelationId={CorrelationId}", correlationId);
            ExceptionDispatchInfo.Capture(ex).Throw();
            return;
        }

        _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}, Method={Method}, Path={Path}",
            correlationId, context.Request.Method, context.Request.Path);

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problem);
    }
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}
