namespace Faborite.Api.Authentication;

public class AuthenticationSettings
{
    public bool Enabled { get; set; }
    public ApiKeyAuthenticationOptions ApiKey { get; set; } = new();
}

public class ApiKeyAuthenticationOptions
{
    public bool Enabled { get; set; }
    public string HeaderName { get; set; } = "X-API-Key";
}
