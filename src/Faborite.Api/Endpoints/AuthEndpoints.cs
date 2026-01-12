using Faborite.Api.Services;
using Faborite.Core.Configuration;

namespace Faborite.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/connect", async (ConnectRequest request, FaboriteApiService service) =>
        {
            var config = new FaboriteConfig
            {
                WorkspaceId = request.WorkspaceId,
                LakehouseId = request.LakehouseId,
                Auth = new AuthConfig
                {
                    Method = Enum.Parse<AuthMethod>(request.AuthMethod ?? "Default", true),
                    TenantId = request.TenantId,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret
                }
            };

            var success = await service.ConnectAsync(config);
            
            return success 
                ? Results.Ok(new { Connected = true, Message = "Successfully connected to OneLake" })
                : Results.BadRequest(new { Connected = false, Message = "Failed to connect. Check credentials and IDs." });
        })
        .WithName("Connect")
        ;

        group.MapPost("/disconnect", (FaboriteApiService service) =>
        {
            service.Disconnect();
            return Results.Ok(new { Message = "Disconnected" });
        })
        .WithName("Disconnect")
        ;

        group.MapGet("/status", (FaboriteApiService service) =>
        {
            return Results.Ok(new
            {
                IsConnected = service.IsConnected,
                WorkspaceId = service.CurrentConfig?.WorkspaceId,
                LakehouseId = service.CurrentConfig?.LakehouseId
            });
        })
        .WithName("GetConnectionStatus")
        ;
    }
}

public record ConnectRequest(
    string WorkspaceId,
    string LakehouseId,
    string? AuthMethod = "Default",
    string? TenantId = null,
    string? ClientId = null,
    string? ClientSecret = null
);
