using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Adapters.ACC;
using SimCoach.Adapters.ACC.SharedMemory;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;

namespace SimCoach.App;

/// <summary>
/// Wires the telemetry pipeline: source (live ACC shared memory or MCAP replay, selected by
/// <c>Telemetry:Source</c>) → <see cref="IngestService"/> → <see cref="TelemetryFanOut"/> →
/// {<see cref="SessionManager"/>, <see cref="McapRecorderService"/>}. Session identity is owned by
/// the producer and shared via <see cref="SessionContext"/> (ADR-0011).
/// </summary>
internal static class TelemetryComposition
{
    public static HostApplicationBuilder AddTelemetryPipeline(this HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);

        IngestOptions ingestOptions =
            builder.Configuration.GetSection("Telemetry:Ingest").Get<IngestOptions>() ?? new IngestOptions();
        ingestOptions.EnsureValid();
        builder.Services.AddSingleton(ingestOptions);
        builder.Services.AddSingleton<TelemetryFanOut>();
        builder.Services.AddSingleton<SessionContext>();

        AddTelemetrySource(builder);
        AddStorage(builder);

        // Resolve-time factory so the fallback warning (M2: never silently ignore explicit
        // configuration) can reach the logger, which does not exist at composition time.
        builder.Services.AddSingleton(provider =>
        {
            RecordingOptions recordingOptions = BuildRecordingOptions(
                builder.Configuration, provider.GetRequiredService<ILogger<McapRecorderService>>());
            recordingOptions.EnsureValid();
            return recordingOptions;
        });

        // Stop order is the reverse of registration. SessionManager is registered first so it stops
        // LAST and finalizes the row only after compute (PR-E) has drained its laps. SessionManager
        // and the recorder both subscribe to the fan-out in their constructors, so the opening
        // frames of a session always reach them; IngestService (the producer) is registered last.
        builder.Services.AddHostedService<SessionManager>();
        builder.Services.AddHostedService<McapRecorderService>();
        builder.Services.AddHostedService<IngestService>();
        return builder;
    }

    private static void AddStorage(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider =>
        {
            DatabaseOptions databaseOptions = BuildDatabaseOptions(
                builder.Configuration, provider.GetRequiredService<ILogger<DatabaseMigrator>>());
            databaseOptions.EnsureValid();
            return databaseOptions;
        });
        builder.Services.AddSingleton<SqliteConnectionFactory>();
        builder.Services.AddSingleton<DatabaseMigrator>();
        builder.Services.AddSingleton<SessionRepository>();
        builder.Services.AddSingleton<LapRepository>();
    }

    private static void AddTelemetrySource(HostApplicationBuilder builder)
    {
        string sourceKind = builder.Configuration["Telemetry:Source"] ?? AccSharedMemoryReader.SimId;
        switch (sourceKind.ToLowerInvariant())
        {
            case AccSharedMemoryReader.SimId:
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException(
                        "The 'acc' telemetry source reads Windows shared memory. On this OS set "
                        + "Telemetry:Source=replay (env: SIMCOACH_Telemetry__Source=replay).");
                }

                AddAccSource(builder);
                break;

            case McapReplaySource.SimId:
                ReplayOptions replayOptions =
                    builder.Configuration.GetSection("Telemetry:Replay").Get<ReplayOptions>() ?? new ReplayOptions();
                replayOptions.EnsureValid();
                builder.Services.AddSingleton(replayOptions);
                builder.Services.AddSingleton<ITelemetrySource, McapReplaySource>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Telemetry:Source '{sourceKind}' — expected "
                    + $"'{AccSharedMemoryReader.SimId}' or '{McapReplaySource.SimId}'.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddAccSource(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new AccReaderOptions());
        builder.Services.AddSingleton<IAccPageSource, MemoryMappedAccPageSource>();
        builder.Services.AddSingleton<ITelemetrySource>(provider => new AccSharedMemoryReader(
            provider.GetRequiredService<IAccPageSource>(),
            AccFrameMapper.Map,
            AccFrameMapper.IsRecordable,
            provider.GetRequiredService<AccReaderOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<AccSharedMemoryReader>>()));
    }

    private static DatabaseOptions BuildDatabaseOptions(IConfiguration configuration, ILogger logger)
    {
        DatabaseOptions defaults = new();
        string configured = configuration["Database:DbPath"] ?? string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(configured);
        // An unexpanded %VAR% (e.g. %LOCALAPPDATA% outside Windows) means "use the default".
        bool useDefault = string.IsNullOrWhiteSpace(expanded) || expanded.Contains('%');
        if (useDefault && !string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning(
                "Database:DbPath '{Configured}' contains an unexpandable token; using {Fallback}",
                configured,
                defaults.DbPath);
        }

        return useDefault ? defaults : new DatabaseOptions { DbPath = expanded };
    }

    private static RecordingOptions BuildRecordingOptions(IConfiguration configuration, ILogger logger)
    {
        RecordingOptions defaults = new();
        string configured = configuration["Storage:DataRoot"] ?? string.Empty;
        string dataRoot = Environment.ExpandEnvironmentVariables(configured);
        // An unexpanded %VAR% (e.g. %LOCALAPPDATA% outside Windows) means "use the default".
        bool useDefault = string.IsNullOrWhiteSpace(dataRoot) || dataRoot.Contains('%');
        if (useDefault && !string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning(
                "Storage:DataRoot '{Configured}' contains an unexpandable token; recordings go to {Fallback}",
                configured,
                defaults.BasePath);
        }

        return new RecordingOptions
        {
            BasePath = useDefault ? defaults.BasePath : Path.Combine(dataRoot, "recordings"),
            SegmentDuration = TimeSpan.FromSeconds(configuration.GetValue("Storage:McapRotateSeconds", 60)),
        };
    }
}
