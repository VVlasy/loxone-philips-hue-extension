using LoxoneHueBridge.Core.Extensions;
using LoxoneHueBridge.Web.Services;
using LoxoneHueBridge.Web.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Serilog;

// Initial Serilog configuration (will be reconfigured later with custom sinks)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting LoxoneHueBridge Web application");
    
    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // Add services
    builder.Services.AddRazorPages();
    builder.Services.AddControllers();
    builder.Services.AddSignalR();
    
    // Add log collector service
    builder.Services.AddSingleton<ILogCollectorService, LogCollectorService>();
    
    // Add Core services
    builder.Services.AddLoxoneHueBridgeCore(builder.Configuration);

    var app = builder.Build();

    // Reconfigure Serilog with custom sinks now that services are available
    var logCollector = app.Services.GetRequiredService<ILogCollectorService>();
    var hubContext = app.Services.GetRequiredService<IHubContext<LoggingHub>>();
    
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(app.Configuration)
        .WriteTo.LogCollectorSink(logCollector, hubContext)
        .CreateLogger();

    // Configure the HTTP request pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.MapRazorPages();
    app.MapControllers();
    app.MapHub<LoggingHub>("/loggingHub");

    // Log the URLs the application will listen on
    var urls = app.Configuration["ASPNETCORE_URLS"] ?? 
               app.Configuration.GetSection("Kestrel:Endpoints:Http:Url").Value ?? 
               "http://localhost:5070";
    
    // Log each URL separately for VS Code to detect properly
    foreach (var url in urls.Split(';'))
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Log.Information("Now listening on: {Url}", url.Trim());
        }
    }
    
    Log.Information("LoxoneHueBridge Web application started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Simple SignalR Hub for real-time logging
public class LoggingHub : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}
