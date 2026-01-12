using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Blazored.LocalStorage;
using Faborite.Web;
using Faborite.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient for API calls
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5001") 
});

// MudBlazor
builder.Services.AddMudServices();

// Local storage
builder.Services.AddBlazoredLocalStorage();

// App services
builder.Services.AddScoped<FaboriteApiClient>();
builder.Services.AddScoped<SyncHubService>();
builder.Services.AddScoped<AppStateService>();

await builder.Build().RunAsync();
