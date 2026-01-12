using Faborite.Api.Hubs;
using Faborite.Api.Services;
using Faborite.Core.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace Faborite.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sync").WithTags("Sync");

        group.MapPost("/start", async (
            SyncRequest request,
            FaboriteApiService service,
            SyncProgressService progressService,
            IHubContext<SyncHub> hubContext,
            CancellationToken cancellationToken) =>
        {
            if (!service.IsConnected)
                return Results.BadRequest(new { Error = "Not connected. Connect first." });

            var config = service.CurrentConfig!;
            var sessionId = progressService.CreateSession(config.WorkspaceId!, config.LakehouseId!);

            // Start sync in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var sampleOverride = request.SampleConfig != null ? new SampleConfig
                    {
                        Strategy = Enum.Parse<SampleStrategy>(request.SampleConfig.Strategy ?? "Random", true),
                        Rows = request.SampleConfig.Rows ?? 10000,
                        DateColumn = request.SampleConfig.DateColumn,
                        StratifyColumn = request.SampleConfig.StratifyColumn,
                        WhereClause = request.SampleConfig.WhereClause
                    } : null;

                    var formatOverride = request.FormatConfig != null ? new FormatConfig
                    {
                        Format = Enum.Parse<OutputFormat>(request.FormatConfig.Format ?? "Parquet", true),
                        Compression = request.FormatConfig.Compression ?? "snappy",
                        SingleFile = request.FormatConfig.SingleFile ?? true
                    } : null;

                    var progress = new Progress<(string tableName, int current, int total)>(async p =>
                    {
                        progressService.UpdateProgress(sessionId, p.tableName, p.current, p.total, 0);
                        await hubContext.BroadcastProgress(sessionId, p.tableName, p.current, p.total, 0);
                    });

                    var summary = await service.SyncAsync(
                        request.Tables,
                        sampleOverride,
                        formatOverride,
                        progress,
                        cancellationToken);

                    progressService.CompleteSession(sessionId, summary.FailedTables == 0);
                    await hubContext.BroadcastSyncCompleted(
                        sessionId,
                        summary.FailedTables == 0,
                        summary.SuccessfulTables,
                        summary.TotalRows,
                        summary.Duration);
                }
                catch (Exception ex)
                {
                    progressService.CompleteSession(sessionId, false);
                    await hubContext.Clients.Group(sessionId).SendAsync("SyncError", new
                    {
                        SessionId = sessionId,
                        Error = ex.Message
                    });
                }
            }, cancellationToken);

            return Results.Accepted($"/api/sync/status/{sessionId}", new
            {
                SessionId = sessionId,
                Message = "Sync started",
                StatusUrl = $"/api/sync/status/{sessionId}"
            });
        })
        .WithName("StartSync")
        ;

        group.MapGet("/status/{sessionId}", (string sessionId, SyncProgressService progressService) =>
        {
            var session = progressService.GetSession(sessionId);
            if (session == null)
                return Results.NotFound(new { Error = "Session not found" });

            return Results.Ok(session);
        })
        .WithName("GetSyncStatus")
        ;

        group.MapPost("/cancel/{sessionId}", (string sessionId, SyncProgressService progressService) =>
        {
            var session = progressService.GetSession(sessionId);
            if (session == null)
                return Results.NotFound(new { Error = "Session not found" });

            // Note: Actual cancellation would need CancellationTokenSource management
            progressService.CompleteSession(sessionId, false);
            return Results.Ok(new { Message = "Cancellation requested" });
        })
        .WithName("CancelSync")
        ;

        group.MapGet("/history", (SyncProgressService progressService, int? count) =>
        {
            var sessions = progressService.GetRecentSessions(count ?? 10);
            return Results.Ok(sessions);
        })
        .WithName("GetSyncHistory")
        ;
    }
}

public record SyncRequest(
    string[]? Tables = null,
    SampleConfigDto? SampleConfig = null,
    FormatConfigDto? FormatConfig = null
);

public record SampleConfigDto(
    string? Strategy = "Random",
    int? Rows = 10000,
    string? DateColumn = null,
    string? StratifyColumn = null,
    string? WhereClause = null
);

public record FormatConfigDto(
    string? Format = "Parquet",
    string? Compression = "snappy",
    bool? SingleFile = true
);
