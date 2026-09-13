using Hangfire;
using Hangfire.Dashboard;
using HangfireDemo.Api.Security;
using HangfireDemo.Core.Jobs.Configuration;
using HangfireDemo.Core.Jobs.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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
    options.AddPolicy(
        SecurityPolicies.HangfireOperator,
        policy => policy.RequireRole(
            SecurityPolicies.HangfireOperator,
            SecurityPolicies.HangfireAdmin));
});

builder.Services.AddControllers();
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
app.UseAuthentication();
app.UseAuthorization();

var dashboardAuthorization =
    app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Company Hangfire",
    Authorization = [dashboardAuthorization],
    IsReadOnlyFunc = context =>
        !context.GetHttpContext().User.IsInRole(SecurityPolicies.HangfireAdmin)
});

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/hangfire"));

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
