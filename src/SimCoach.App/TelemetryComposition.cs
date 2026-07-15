using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Adapters.ACC;
using SimCoach.Adapters.ACC.SharedMemory;
using SimCoach.Coach.Rules;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;

namespace SimCoach.App;

/// <summary>
/// Wires the full host: the telemetry pipeline (source → <see cref="IngestService"/> → <see cref="TelemetryFanOut"/>
/// → {<see cref="SessionManager"/>, <see cref="McapRecorderService"/>, <see cref="ComputeService"/>}) plus the
/// Coach + LLM stack (<c>AddCoachStack</c>, slotted into the hosted-service order). Session identity is owned by
/// the producer and shared via <see cref="SessionContext"/> (ADR-0011). Public so App.Tests can build the same host.
/// </summary>
public static class TelemetryComposition
{
    public static HostApplicationBuilder AddTelemetryPipeline(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(TimeProvider.System);

        IngestOptions ingestOptions =
            builder.Configuration.GetSection("Telemetry:Ingest").Get<IngestOptions>() ?? new IngestOptions();
        ingestOptions.EnsureValid();
        builder.Services.AddSingleton(ingestOptions);
        builder.Services.AddSingleton<TelemetryFanOut>();
        builder.Services.AddSingleton<SessionContext>();

        AddTelemetrySource(builder);
        AddStorage(builder);
        AddCompute(builder);

        // Resolve-time factory so the fallback warning (M2: never silently ignore explicit
        // configuration) can reach the logger, which does not exist at composition time.
        builder.Services.AddSingleton(provider =>
        {
            RecordingOptions recordingOptions = BuildRecordingOptions(
                builder.Configuration, provider.GetRequiredService<ILogger<McapRecorderService>>());
            recordingOptions.EnsureValid();
            return recordingOptions;
        });

        // OptimalReferenceBaker (M46) is a StartAsync one-shot catch-up bake, NOT a stop-order
        // participant (its StopAsync is a no-op). It is registered before the load-bearing services so
        // the historical optimal is refreshed at start; because it takes no part in shutdown, its
        // position does not disturb the reversed stop-order among SessionManager/recorder/coach/compute/
        // ingest below.
        builder.Services.AddHostedService<OptimalReferenceBaker>();

        // Stop order is the reverse of registration. SessionManager is registered first so it stops
        // LAST and finalizes the row (counts/PB from persisted laps, plus laps.parquet conversion)
        // only after ComputeService has drained and written its lap rows and the recorder has flushed
        // its segments. All consumers subscribe to the fan-out in their constructors, so the opening
        // frames of a session always reach them; IngestService (the producer) is registered last.
        builder.Services.AddHostedService<SessionManager>();
        builder.Services.AddHostedService<McapRecorderService>();

        // Coach + LLM stack: its two hosted services (CoachService, then LiveCoachAmbientState) slot HERE so
        // they stop after ComputeService completes the domain-event fan-out — CoachService drains it to
        // completion to emit the final debrief — and before SessionManager finalizes the session row.
        builder.AddCoachStack();

        builder.Services.AddHostedService<ComputeService>();
        builder.Services.AddHostedService<IngestService>();
        return builder;
    }

    private static void AddStorage(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider =>
        {
            DatabaseOptions databaseOptions = ResolveDatabaseOptions(
                builder.Configuration, provider.GetRequiredService<ILogger<DatabaseMigrator>>());
            databaseOptions.EnsureValid();
            return databaseOptions;
        });
        builder.Services.AddSingleton<SqliteConnectionFactory>();
        // No DatabaseMigrator registration: Program migrates via a manually-constructed instance before
        // Build() (the settings config source must read a migrated table at config-build time).
        builder.Services.AddSingleton<SessionRepository>();
        builder.Services.AddSingleton<LapRepository>();
        builder.Services.AddSingleton<ReferenceRepository>();
        builder.Services.AddSingleton<ReferenceSnapshotRepository>();
        // Sim-agnostic seam (Storage) bridged to the ACC catalog at the composition edge; consumed by
        // SessionManager's laps.parquet conversion and the compute track model + resampler.
        builder.Services.AddSingleton<ITrackLengthProvider, AccTrackLengthProvider>();
    }

    private static void AddCompute(HostApplicationBuilder builder)
    {
        string dataRoot = ResolveDataRoot(builder.Configuration);

        // M9: the apex-band fraction is a SINGLE knob. It is owned by the Coach live gate
        // (Coach:Rules:ApexWindowFraction) and fed here into the brake-overlap metric so both share one
        // definition of "apex" and cannot drift. Bind that one source and override any stray
        // Compute:ApexWindowFraction, then assert equality as defense-in-depth.
        double apexWindowFraction =
            builder.Configuration.GetSection("Coach:Rules").Get<RuleEngineOptions>()?.ApexWindowFraction
            ?? new RuleEngineOptions().ApexWindowFraction;

        ComputeOptions computeOptions =
            (builder.Configuration.GetSection("Compute").Get<ComputeOptions>() ?? new ComputeOptions())
            with
            { ApexWindowFraction = apexWindowFraction };
        computeOptions.EnsureValid();
        if (computeOptions.ApexWindowFraction != apexWindowFraction)
        {
            throw new InvalidOperationException(
                "ComputeOptions.ApexWindowFraction must equal Coach:Rules:ApexWindowFraction (single shared apex band).");
        }

        builder.Services.AddSingleton(computeOptions);

        builder.Services.AddSingleton(CornerGeometryDataset.Load());
        builder.Services.AddSingleton(CenterlineGeometryDataset.Load());
        builder.Services.AddSingleton<TrackModelStore>();

        builder.Services.AddSingleton(new ReferenceStorageOptions
        {
            Directory = Path.Combine(dataRoot, "references"),
        });
        builder.Services.AddSingleton<ReferenceLookup>();
        builder.Services.AddSingleton<OptimalReferenceLookup>();
        builder.Services.AddSingleton<ReferenceStore>();

        OptimalReferenceOptions optimalOptions =
            builder.Configuration.GetSection("Reference:Optimal").Get<OptimalReferenceOptions>()
            ?? new OptimalReferenceOptions();
        optimalOptions.EnsureValid();
        builder.Services.AddSingleton(optimalOptions);

        builder.Services.AddSingleton<DomainEventFanOut>();
    }

    /// <summary>
    /// The single resolver for the data root behind recordings/references/track_models —
    /// <c>Storage:DataRoot</c> with <c>%VAR%</c> expansion, falling back to the platform default when
    /// unset or unexpandable. <see cref="BuildRecordingOptions"/> derives the recordings dir from this
    /// same call so all three subtrees can never drift to different roots.
    /// </summary>
    private static string ResolveDataRoot(IConfiguration configuration)
    {
        string configured = configuration["Storage:DataRoot"] ?? string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(configured);
        bool useDefault = string.IsNullOrWhiteSpace(expanded) || expanded.Contains('%');
        return useDefault
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimCoach")
            : expanded;
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
        AccReaderOptions accOptions = new()
        {
            AllowReplayCapture = builder.Configuration.GetValue<bool>("Telemetry:Acc:AllowReplayCapture"),
        };
        builder.Services.AddSingleton(accOptions);
        builder.Services.AddSingleton<IAccPageSource, MemoryMappedAccPageSource>();
        builder.Services.AddSingleton<ITelemetrySource>(provider => new AccSharedMemoryReader(
            provider.GetRequiredService<IAccPageSource>(),
            AccFrameMapper.Map,
            snapshot => AccFrameMapper.IsRecordable(snapshot, accOptions.AllowReplayCapture),
            provider.GetRequiredService<AccReaderOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<AccSharedMemoryReader>>()));
    }

    /// <summary>
    /// The single resolver for <c>Database:DbPath</c> (with <c>%VAR%</c> expansion, falling back to the default
    /// on an unexpandable token). Public so <c>Program</c> can build the same <see cref="DatabaseOptions"/> to
    /// migrate + open the settings configuration source <em>before</em> <c>Build()</c>, while the DI factory uses
    /// it again with a real logger — so every path resolves to one database.
    /// </summary>
    public static DatabaseOptions ResolveDatabaseOptions(IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DatabaseOptions defaults = new();
        string configured = configuration["Database:DbPath"] ?? string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(configured);
        // An unexpanded %VAR% (e.g. %LOCALAPPDATA% outside Windows) means "use the default".
        bool useDefault = string.IsNullOrWhiteSpace(expanded) || expanded.Contains('%');
        if (useDefault && !string.IsNullOrWhiteSpace(configured))
        {
            logger?.LogWarning(
                "Database:DbPath '{Configured}' contains an unexpandable token; using {Fallback}",
                configured,
                defaults.DbPath);
        }

        return useDefault ? defaults : new DatabaseOptions { DbPath = expanded };
    }

    /// <summary>
    /// Inserts a configuration source directly BELOW the last-added source. Program adds the <c>SIMCOACH_</c>
    /// env source last, so this slots the SQLite settings source between the JSON files and env: a stored row
    /// overrides the JSON, but a deliberate <c>SIMCOACH_</c> override still wins. Public so the precedence is
    /// testable without invoking <c>Program.Main</c>.
    /// </summary>
    public static void InsertSourceBelowLast(IConfigurationBuilder builder, IConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        builder.Sources.Insert(builder.Sources.Count - 1, source);
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
            // Non-default root comes from the shared resolver (== dataRoot here) so recordings and the
            // references/track_models dirs built off ResolveDataRoot stay on one root.
            BasePath = useDefault ? defaults.BasePath : Path.Combine(ResolveDataRoot(configuration), "recordings"),
            SegmentDuration = TimeSpan.FromSeconds(configuration.GetValue("Storage:McapRotateSeconds", 60)),
        };
    }
}
