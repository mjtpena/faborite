package com.faborite.sdk;

import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.URI;
import java.util.List;
import java.util.Map;
import com.google.gson.Gson;

/**
 * Faborite Java SDK
 * Issue #128
 * 
 * Installation (Maven):
 *   <dependency>
 *     <groupId>com.faborite</groupId>
 *     <artifactId>faborite-sdk</artifactId>
 *     <version>1.0.0</version>
 *   </dependency>
 * 
 * Usage:
 *   FaboriteClient client = new FaboriteClient("your-api-key");
 *   SyncResponse response = client.triggerSync("workspace-id", "lakehouse-id");
 */
public class FaboriteClient {
    private final String apiKey;
    private final String baseUrl;
    private final HttpClient httpClient;
    private final Gson gson;

    public FaboriteClient(String apiKey) {
        this(apiKey, "https://api.faborite.com");
    }

    public FaboriteClient(String apiKey, String baseUrl) {
        this.apiKey = apiKey;
        this.baseUrl = baseUrl;
        this.httpClient = HttpClient.newHttpClient();
        this.gson = new Gson();
    }

    public SyncResponse triggerSync(String workspaceId, String lakehouseId) throws Exception {
        return triggerSync(workspaceId, lakehouseId, null);
    }

    public SyncResponse triggerSync(String workspaceId, String lakehouseId, SyncOptions options) 
            throws Exception {
        var request = new SyncRequest(workspaceId, lakehouseId, options);
        var json = gson.toJson(request);

        var httpRequest = HttpRequest.newBuilder()
            .uri(URI.create(baseUrl + "/api/sync"))
            .header("Authorization", "Bearer " + apiKey)
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(json))
            .build();

        var response = httpClient.send(httpRequest, HttpResponse.BodyHandlers.ofString());
        return gson.fromJson(response.body(), SyncResponse.class);
    }

    public List<String> listTables(String workspaceId, String lakehouseId) throws Exception {
        var httpRequest = HttpRequest.newBuilder()
            .uri(URI.create(String.format("%s/api/tables?workspaceId=%s&lakehouseId=%s", 
                baseUrl, workspaceId, lakehouseId)))
            .header("Authorization", "Bearer " + apiKey)
            .GET()
            .build();

        var response = httpClient.send(httpRequest, HttpResponse.BodyHandlers.ofString());
        return gson.fromJson(response.body(), List.class);
    }

    public SyncStatus getSyncStatus(String syncId) throws Exception {
        var httpRequest = HttpRequest.newBuilder()
            .uri(URI.create(baseUrl + "/api/sync/" + syncId))
            .header("Authorization", "Bearer " + apiKey)
            .GET()
            .build();

        var response = httpClient.send(httpRequest, HttpResponse.BodyHandlers.ofString());
        return gson.fromJson(response.body(), SyncStatus.class);
    }

    public DataProfile profileData(String tableName) throws Exception {
        var request = Map.of("tableName", tableName);
        var json = gson.toJson(request);

        var httpRequest = HttpRequest.newBuilder()
            .uri(URI.create(baseUrl + "/api/profile"))
            .header("Authorization", "Bearer " + apiKey)
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(json))
            .build();

        var response = httpClient.send(httpRequest, HttpResponse.BodyHandlers.ofString());
        return gson.fromJson(response.body(), DataProfile.class);
    }

    // Inner classes for request/response
    public static class SyncRequest {
        private String workspaceId;
        private String lakehouseId;
        private SyncOptions options;

        public SyncRequest(String workspaceId, String lakehouseId, SyncOptions options) {
            this.workspaceId = workspaceId;
            this.lakehouseId = lakehouseId;
            this.options = options;
        }
    }

    public static class SyncOptions {
        private List<String> tables;
        private Integer sampleRows;

        public SyncOptions(List<String> tables, Integer sampleRows) {
            this.tables = tables;
            this.sampleRows = sampleRows;
        }
    }

    public static class SyncResponse {
        private String syncId;
        private String status;
        private String message;

        public String getSyncId() { return syncId; }
        public String getStatus() { return status; }
        public String getMessage() { return message; }
    }

    public static class SyncStatus {
        private String syncId;
        private String status;
        private int progress;

        public String getSyncId() { return syncId; }
        public String getStatus() { return status; }
        public int getProgress() { return progress; }
    }

    public static class DataProfile {
        private String tableName;
        private long rowCount;
        private int columnCount;

        public String getTableName() { return tableName; }
        public long getRowCount() { return rowCount; }
        public int getColumnCount() { return columnCount; }
    }
}
