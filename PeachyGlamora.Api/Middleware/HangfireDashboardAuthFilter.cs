using System.Text;
using Hangfire.Dashboard;

namespace PeachyGlamora.Api.Middleware;

/// <summary>
/// Hangfire's dashboard is plain browser HTML, not an API call — so it can't be protected by
/// our JWT bearer scheme (browsers don't attach an Authorization header on a normal navigation).
/// HTTP Basic Auth is the standard, widely-used way to lock this down: the browser prompts for
/// credentials once and remembers them for the session.
/// Configure real credentials via Hangfire:DashboardUser / Hangfire:DashboardPassword —
/// never leave the defaults in appsettings.json for a deployed environment.
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();

        var expectedUser = config["Hangfire:DashboardUser"];
        var expectedPassword = config["Hangfire:DashboardPassword"];

        if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPassword))
        {
            // Fail closed: if credentials aren't configured, nobody gets in rather than everybody.
            return false;
        }

        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var encoded = authHeader["Basic ".Length..].Trim();
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var separatorIndex = decoded.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var user = decoded[..separatorIndex];
                    var password = decoded[(separatorIndex + 1)..];
                    if (user == expectedUser && password == expectedPassword)
                        return true;
                }
            }
            catch (FormatException)
            {
                // Malformed header — fall through to the 401 challenge below.
            }
        }

        httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Peachy Glamora Admin\"";
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }
}
