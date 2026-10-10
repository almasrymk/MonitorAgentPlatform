using MonitorCloud.Api;
using MonitorCloud.Infrastructure;
using MonitorCloud.Infrastructure.Seeding;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    ProductionReadiness.EnsureReady(builder.Configuration, builder.Environment);
    // The host gets its own logger; the static one stays the bootstrap logger for start-up failures. Several hosts in
    // one process (the test suites) then never close each other's logger.
    builder.Host.UseSerilog(
        (context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    builder.Services.AddApi(builder.Configuration, builder.Environment);

    var app = builder.Build();
    app.Services.LoadFakeLicensingData(app.Environment.ContentRootPath);

    // `dotnet run --project src/MonitorCloud.Api -- seed --reset` drops the demo data and seeds again (08).
    if (args.Length > 0 && args[0] == "seed")
    {
        await app.Services.ApplyMigrationsAsync(force: true);
        await app.Services.SeedAsync(reset: args.Contains("--reset"));
        return;
    }

    app.UseApi();

    await app.Services.ApplyMigrationsAsync();
    await app.Services.SeedAsync();
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Monitor Cloud API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; public so the integration tests can host it.</summary>
public partial class Program;
