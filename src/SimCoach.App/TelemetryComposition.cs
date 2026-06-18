using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Adapters.ACC;
using SimCoach.Adapters.ACC.SharedMemory;
using SimCoach.Pipeline;
using SimCoach.Storage;

namespace SimCoach.App;

/// <summary>
/// Wires the Phase 1 telemetry pipeline: source (live ACC shared memory or MCAP replay,
/// selected by <c>Telemetry:Source</c>) → <see cref="IngestService"/> →
/// <see cref="TelemetryFanOut"/> → <see cref="McapRecorderService"/>.
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

        AddTelemetrySource(builder);

        // Resolve-time factory so the fallback warning (M2: never silently ignore explicit
        // configuration) can reach the logger, which does not exist at composition time.
        builder.Services.AddSingleton(provider =>
        {
            RecordingOptions recordingOptions = BuildRecordingOptions(
                builder.Configuration, provider.GetRequiredService<ILogger<McapRecorderService>>());
            recordingOptions.EnsureValid();
            return recordingOptions;
        });

        // The recorder subscribes to the fan-out in its constructor and is registered before
        // the ingest pump, so the first frames of a session always reach the recording.
        builder.Services.AddHostedService<McapRecorderService>();
        builder.Services.AddHostedService<IngestService>();
        return builder;
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
