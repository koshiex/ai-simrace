using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SimCoach.Coach;
using SimCoach.LLM;
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
