using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Faborite.Api.OAuth;

/// <summary>
/// OAuth2 authentication handler for third-party identity providers.
/// Issue #58
/// </summary>
public class OAuth2Handler : AuthenticationHandler<OAuth2Options>
{
    public OAuth2Handler(
        IOptionsMonitor<OAuth2Options> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader.ToString().Replace("Bearer ", "");
        
        try
        {
            var claims = await ValidateTokenAsync(token);
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "OAuth2 authentication failed");
            return AuthenticateResult.Fail("Invalid token");
        }
    }

    private async Task<List<Claim>> ValidateTokenAsync(string token)
    {
        // In production, validate with identity provider (Auth0, Azure AD, etc.)
        // For now, return placeholder claims
        return new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "user-id"),
            new Claim(ClaimTypes.Name, "user-name"),
            new Claim(ClaimTypes.Email, "user@example.com")
        };
    }
}

public class OAuth2Options : AuthenticationSchemeOptions
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// Multi-tenancy support for isolating data by organization.
/// Issue #61
/// </summary>
public class TenantResolver
{
    private readonly ILogger<TenantResolver> _logger;

    public TenantResolver(ILogger<TenantResolver> logger)
    {
        _logger = logger;
    }

    public string ResolveTenant(HttpContext context)
    {
        // Try header first
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId))
        {
            return tenantId.ToString();
        }

        // Try subdomain
        var host = context.Request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length > 2)
        {
            return parts[0]; // subdomain as tenant
        }

        // Try claim
        var claim = context.User.FindFirst("tenant_id");
        if (claim != null)
        {
            return claim.Value;
        }

        return "default";
    }
}

public class TenantContext
{
    public string TenantId { get; set; } = "default";
    public string Name { get; set; } = "";
    public Dictionary<string, string> Settings { get; set; } = new();
}
