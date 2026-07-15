using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SimCoach.Coach;
using SimCoach.LLM;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.App.Tests;

/// <summary>
/// Smoke-tests the real host composition (AddTelemetryPipeline → AddCoachStack) against the shipped
/// appsettings.json: the Coach + LLM graph resolves, and <c>ValidateOnStart</c> rejects a broken config at
/// start. Forces <c>Telemetry:Source=replay</c> so it runs on any OS (the live ACC source is Windows-only).
/// </summary>
public sealed class HostCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-app-" + Guid.NewGuid().ToString("N"));

    public HostCompositionTests() => Directory.CreateDirectory(_root);

    private HostApplicationBuilder NewBuilder(IDictionary<string, string?>? overrides = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory, // the copied appsettings.json lands here
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telemetry:Source"] = "replay",
            ["Telemetry:Replay:Path"] = _root,
            ["Database:DbPath"] = Path.Combine(_root, "simcoach.db"),
            ["Storage:DataRoot"] = _root,
        });

        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        return builder;
    }

    [Fact]
    public void Host_composes_and_resolves_the_coach_stack()
    {
        HostApplicationBuilder builder = NewBuilder();
        builder.AddTelemetryPipeline();
        using IHost host = builder.Build();

        host.Services.GetRequiredService<ILlmClient>().Should().NotBeNull();
        host.Services.GetRequiredService<ICoachAmbientState>().Should().NotBeNull();
        host.Services.GetServices<IHostedService>().Should().Contain(static s => s is CoachService);
    }

    [Fact]
    public void Optimal_baker_is_registered_and_the_load_bearing_stop_order_is_preserved()
    {
        HostApplicationBuilder builder = NewBuilder();
        builder.AddTelemetryPipeline();
        using IHost host = builder.Build();

        List<IHostedService> hosted = [.. host.Services.GetServices<IHostedService>()];
        List<string> names = [.. hosted.Select(static s => s.GetType().Name)];

        // The baker (a StartAsync one-shot, no-op stop) is present.
        names.Should().Contain(nameof(OptimalReferenceBaker));

        // Registration order == start order; stop order is its reverse. The load-bearing invariant is the
        // relative order of these five: SessionManager registered first (stops LAST, finalizes the row),
        // then the recorder, the coach stack, ComputeService, and IngestService registered last (the
        // producer stops FIRST). Assert that relative order is intact regardless of the baker's slot.
        int session = names.IndexOf(nameof(SessionManager));
        int recorder = names.IndexOf(nameof(McapRecorderService));
        int coach = names.IndexOf(nameof(CoachService));
        int compute = names.IndexOf(nameof(ComputeService));
        int ingest = names.IndexOf(nameof(IngestService));

        session.Should().BeGreaterThanOrEqualTo(0);
        session.Should().BeLessThan(recorder);
        recorder.Should().BeLessThan(coach);
        coach.Should().BeLessThan(compute);
        compute.Should().BeLessThan(ingest);

        // The baker takes no part in the reversed stop-order: it must not sit between the recorder and
        // IngestService, so it can never intrude on the finalize-after-drain ordering.
        int baker = names.IndexOf(nameof(OptimalReferenceBaker));
        (baker < session || baker > ingest).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOnStart_rejects_a_route_with_no_rate()
    {
        // openrouter-google has no rate for "unrated/model" → LlmStartupValidator #1 fails at start.
        HostApplicationBuilder builder = NewBuilder(new Dictionary<string, string?>
        {
            ["Llm:Routes:corner:ModelId"] = "unrated/model",
        });
        builder.AddTelemetryPipeline();
        using IHost host = builder.Build();

        Func<Task> start = async () =>
        {
            await host.StartAsync();
            await host.StopAsync();
        };

        await start.Should().ThrowAsync<OptionsValidationException>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
