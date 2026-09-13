using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<HangfireSecurityOptions> securityOptions,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "HangfireApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var security = securityOptions.Value;

        if (environment.IsDevelopment() &&
            string.IsNullOrWhiteSpace(security.AdminApiKey) &&
            string.IsNullOrWhiteSpace(security.ReaderApiKey))
        {
            return Task.FromResult(CreateSuccess(
                "development-admin",
                SecurityPolicies.HangfireAdmin,
                SecurityPolicies.HangfireOperator,
                SecurityPolicies.HangfireReader));
        }

        var credential = GetCredential(security.HeaderName);
        if (credential is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (Matches(credential, security.AdminApiKey))
        {
            return Task.FromResult(CreateSuccess(
                "api-admin",
                SecurityPolicies.HangfireAdmin,
                SecurityPolicies.HangfireOperator,
                SecurityPolicies.HangfireReader));
        }

        if (Matches(credential, security.ReaderApiKey))
        {
            return Task.FromResult(CreateSuccess(
                "api-reader",
                SecurityPolicies.HangfireReader));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }

    private string? GetCredential(string headerName)
    {
        if (Request.Headers.TryGetValue(headerName, out var values))
        {
            var value = values.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) ||
            !string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return null;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(authorization.Parameter));
            var separator = decoded.IndexOf(':');
            return separator < 0 ? null : decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool Matches(string provided, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private static AuthenticateResult CreateSuccess(string name, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
