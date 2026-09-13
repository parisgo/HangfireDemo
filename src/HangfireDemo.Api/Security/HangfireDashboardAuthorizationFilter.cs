using Hangfire.Dashboard;

namespace HangfireDemo.Api.Security;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;
        var authorized = user.Identity?.IsAuthenticated == true &&
            (user.IsInRole(SecurityPolicies.HangfireAdmin) ||
             user.IsInRole(SecurityPolicies.HangfireReader));

        if (!authorized)
        {
            httpContext.Response.Headers.WWWAuthenticate =
                "Basic realm=\"Hangfire\", charset=\"UTF-8\"";
        }

        return authorized;
    }
}
