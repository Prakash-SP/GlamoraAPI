using System.Net;
using System.Text.Json;

namespace PeachyGlamora.Api.Middleware;

/// <summary>Catches anything that bubbles past controller-level try/catch blocks and returns
/// a consistent JSON error shape instead of ASP.NET's default HTML error page. Registered first
/// in the pipeline (see Program.cs) so it wraps every other middleware and controller.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            var (status, message) = ex switch
            {
                InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "You are not authorized to perform this action."),
                KeyNotFoundException => (HttpStatusCode.NotFound, "The requested resource was not found."),
                _ => (HttpStatusCode.InternalServerError, "Something went wrong on our end. Please try again in a moment.")
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;

            var payload = new
            {
                error = message,
                // Only leak stack traces in Development — never in a deployed environment.
                detail = _env.IsDevelopment() ? ex.ToString() : null
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
