using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SimCoach.Storage.Database;

namespace SimCoach.App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Content root = executable directory: appsettings load from next to the binary
        // regardless of the launch directory, and the default source order keeps the standard
        // precedence (json < env < command line) — re-adding appsettings here would break it.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "SIMCOACH_");

        NormalizeSerilogFilePath(builder.Configuration);
        builder.Services.AddSerilog((services, config) =>
            config.ReadFrom.Configuration(builder.Configuration));

        builder.AddTelemetryPipeline();

        using IHost host = builder.Build();
        // Bring the SQLite schema up to date before any hosted service touches the database.
        host.Services.GetRequiredService<DatabaseMigrator>().Migrate();
        await host.RunAsync();
        return 0;
    }

    /// <summary>
    /// The Serilog file sink path uses %LOCALAPPDATA%, which only expands on Windows; outside
    /// Windows the literal token would become a CWD-relative directory. Expand it here and
    /// fall back to the platform's local-app-data folder when expansion is impossible.
    /// </summary>
    private static void NormalizeSerilogFilePath(IConfigurationManager configuration)
    {
        const string fileSinkPathKey = "Serilog:WriteTo:1:Args:path"; // index 1 = File sink in appsettings.json
        string? configured = configuration[fileSinkPathKey];
        if (configured is null)
        {
            return;
        }

        string expanded = Environment.ExpandEnvironmentVariables(configured);
        configuration[fileSinkPathKey] = expanded.Contains('%')
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SimCoach",
                "logs",
                "simcoach-.log")
            : expanded;
    }
}
