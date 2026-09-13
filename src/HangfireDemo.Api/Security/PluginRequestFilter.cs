using HangfireDemo.Core.Plugins;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Api.Security;

public sealed class PluginRequestFilter(IAntiforgery antiforgery, IOptions<HangfireSecurityOptions> security)
    : IAsyncActionFilter, IExceptionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
            string.IsNullOrWhiteSpace(request.Headers[security.Value.HeaderName]))
        {
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException)
            {
                context.Result = new BadRequestObjectResult(new { error = "Invalid anti-forgery token. Reload the manager page." });
                return;
            }
        }
        await next();
    }

    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            PluginValidationException => 400,
            PluginConflictException => 409,
            PluginNotFoundException => 404,
            _ => 0
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new { error = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
