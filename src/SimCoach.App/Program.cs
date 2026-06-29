using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SimCoach.Storage.Configuration;
using SimCoach.Storage.Database;

namespace SimCoach.App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Content root = executable directory: appsettings load from next to the binary regardless of the
        // launch directory; re-adding appsettings here would disturb the default source order.
        // NOTE: the layers appended below (appsettings.Local.json, SIMCOACH_ env, and the inserted SQLite
        // settings source) are added AFTER the default command-line source, so they outrank `--Foo=bar`
        // args. That is intentional — this host's override surface is SIMCOACH_ env + stored settings (the
        // documented replay loop), not the command line; env still wins over a stored settings row.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "SIMCOACH_");

        // Migrate the schema and open the SQLite-backed settings configuration source BEFORE Build(): the source
        // reads the settings table at config-build time so a stored model/budget/Llm:Live override binds into
        // IOptionsMonitor<LlmOptions> / the RuleEngine budget. DbPath is resolved from the same resolver the DI
        // factory uses, so both open one database.
        DatabaseOptions databaseOptions = TelemetryComposition.ResolveDatabaseOptions(builder.Configuration);
        databaseOptions.EnsureValid();
        var connectionFactory = new SqliteConnectionFactory(databaseOptions);
        new DatabaseMigrator(connectionFactory).Migrate();

        // Insert the settings source just BELOW the SIMCOACH_ env source (the last source added) so a deliberate
        // env override still wins over a stored row — preserving the documented replay override loop.
        var settingsSource = new SqliteSettingsConfigurationSource(connectionFactory);
        TelemetryComposition.InsertSourceBelowLast(builder.Configuration, settingsSource);
        builder.Services.AddSingleton<ISettingsReloadSignal>(settingsSource);

        NormalizeSerilogFilePath(builder.Configuration);
        builder.Services.AddSerilog((services, config) =>
            config.ReadFrom.Configuration(builder.Configuration));

        builder.AddTelemetryPipeline();

        using IHost host = builder.Build();
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
