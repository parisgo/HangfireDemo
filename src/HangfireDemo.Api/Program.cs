using Hangfire;
using Hangfire.Dashboard;
using HangfireDemo.Api.Security;
using HangfireDemo.Core.Plugins;
using HangfireDemo.Core.Jobs.Configuration;
using HangfireDemo.Core.Jobs.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var builder = WebApplication.CreateBuilder(args);
HangfireDemo.Core.Logging.ApplicationLogging.AddApplicationLogging(builder.Logging,
    builder.Configuration, builder.Environment.ContentRootPath, "Api");

var security = builder.Configuration
    .GetSection(HangfireSecurityOptions.SectionName)
    .Get<HangfireSecurityOptions>() ?? new HangfireSecurityOptions();

if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(security.AdminApiKey))
{
    throw new InvalidOperationException(
        "Security:AdminApiKey is required outside Development.");
}

builder.Services.AddOptions<HangfireSecurityOptions>()
    .Bind(builder.Configuration.GetSection(HangfireSecurityOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.HeaderName),
        "Security:HeaderName is required.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SecurityPolicies.HangfireAdmin, policy => policy.RequireRole(SecurityPolicies.HangfireAdmin));
    options.AddPolicy(SecurityPolicies.HangfireReader, policy => policy.RequireRole(
        SecurityPolicies.HangfireReader, SecurityPolicies.HangfireOperator, SecurityPolicies.HangfireAdmin));
    options.AddPolicy(
        SecurityPolicies.HangfireOperator,
        policy => policy.RequireRole(
            SecurityPolicies.HangfireOperator,
            SecurityPolicies.HangfireAdmin));
});

builder.Services.AddControllers();
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/", SecurityPolicies.HangfireReader));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddScoped<PluginRequestFilter>();
builder.Services.AddPluginStorage(builder.Configuration);
builder.Services.AddScoped<PluginScheduling>();
builder.Services.AddScoped<PluginExecutions>();
builder.Services.AddScoped<HangfireDemo.Core.WebApi.WebApiScheduling>();
builder.Services.AddScoped<PluginDashboardNames>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Hangfire operator key",
        In = ParameterLocation.Header,
        Name = security.HeaderName,
        Type = SecuritySchemeType.ApiKey
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "ApiKey"
            }
        }] = Array.Empty<string>()
    });
});

builder.Services.AddHangfirePersistence(builder.Configuration);

builder.Services.AddSingleton<HangfireDashboardAuthorizationFilter>();
builder.Services.AddHealthChecks()
    .AddCheck<HangfireServerHealthCheck>("hangfire", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var dashboardAuthorization =
    app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hangfire") &&
        (context.User.IsInRole(SecurityPolicies.HangfireReader) ||
         context.User.IsInRole(SecurityPolicies.HangfireOperator) ||
         context.User.IsInRole(SecurityPolicies.HangfireAdmin)))
        await context.RequestServices.GetRequiredService<PluginDashboardNames>().LoadAsync(context.RequestAborted);
    await next(context);
});

var dashboardOptions = new DashboardOptions
{
    DashboardTitle = "Company Hangfire",
    AppPath = "/job-manager",
    Authorization = [dashboardAuthorization],
    IsReadOnlyFunc = context =>
        !context.GetHttpContext().User.IsInRole(SecurityPolicies.HangfireAdmin)
};
var defaultDisplayName = dashboardOptions.DisplayNameFunc;
dashboardOptions.DisplayNameFunc = (context, job) =>
    context.GetHttpContext().RequestServices.GetRequiredService<PluginDashboardNames>().Resolve(job)
    ?? defaultDisplayName?.Invoke(context, job) ?? job.ToString();
app.UseHangfireDashboard("/hangfire", dashboardOptions);

app.MapControllers();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/job-manager"));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

public partial class Program;
