using Faborite.Api.Authentication;
using Faborite.Api.Endpoints;
using Faborite.Api.Hubs;
using Faborite.Api.Services;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Faborite API",
        Version = "v1",
        Description = "API for syncing Microsoft Fabric lakehouse data locally for faster development",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Michael John Peña",
            Email = "michael@datachain.consulting",
            Url = new Uri("https://github.com/mjtpena/faborite")
        },
        License = new Microsoft.OpenApi.Models.OpenApiLicense
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });
    
    // Add API Key security definition
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-API-Key",
        Description = "API Key authentication"
    });
});

// Authentication (optional - controlled by config)
var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");
if (authEnabled)
{
    builder.Services.AddAuthentication("ApiKey")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);
    builder.Services.AddAuthorization();
}

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
    {
        policy.WithOrigins("http://localhost:5002", "https://localhost:5002", "http://localhost:5003")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register services
builder.Services.AddSingleton<SyncProgressService>();
builder.Services.AddScoped<FaboriteApiService>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Faborite API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowBlazor");

// Use authentication if enabled
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Map endpoints
app.MapAuthEndpoints();
app.MapTablesEndpoints();
app.MapSyncEndpoints();
app.MapConfigEndpoints();
app.MapLocalDataEndpoints();
app.MapQueryEndpoints();

// Map SignalR hub
app.MapHub<SyncHub>("/hubs/sync");

// Map health checks
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Version = "0.1.1"
})).WithTags("Health");

app.Run();

// Make Program accessible for testing
public partial class Program { }
