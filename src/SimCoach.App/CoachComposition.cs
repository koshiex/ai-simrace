using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimCoach.Coach;
using SimCoach.LLM;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.App;

/// <summary>
/// Wires the Coach + LLM stack into the live host: the App-edge sim-agnostic bridges (car-class, session-id),
/// the settings write-side, then the LLM and Coach rings (<see cref="LlmServiceCollectionExtensions.AddLlm"/> /
/// <see cref="CoachServiceCollectionExtensions.AddCoaching"/>). Invoked by <c>AddTelemetryPipeline</c> between the
/// recorder and ComputeService registrations so the Coach hosted services land at the right point in the
/// load-bearing stop order.
/// </summary>
internal static class CoachComposition
{
    public static HostApplicationBuilder AddCoachStack(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // App-edge sim-agnostic bridges (mirror AccTrackLengthProvider): App is the only project that may
        // reference the ACC adapter / the pipeline's SessionContext, so the wrappers live here.
        builder.Services.AddSingleton<ICarClassProvider, AccCarClassProvider>();
        builder.Services.AddSingleton<ISessionIdProvider, SessionContextSessionIdProvider>();

        // Settings write-side: a write re-binds IOptionsMonitor<LlmOptions> via the SQLite configuration source
        // registered as ISettingsReloadSignal in Program (the reload signal is the same source instance).
        builder.Services.AddSingleton<SettingsRepository>();
        builder.Services.AddSingleton<ISettingsStore, SqliteSettingsStore>();

        builder.Services.AddLlm(builder.Configuration);
        builder.Services.AddCoaching(builder.Configuration);
        return builder;
    }
}
