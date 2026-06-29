using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SimCoach.Storage.Configuration;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.App.Tests;

/// <summary>
/// Proves the load-bearing config-source precedence Program assembles via
/// <see cref="TelemetryComposition.InsertSourceBelowLast"/>: a <c>SIMCOACH_</c> env override beats a stored
/// settings row, which beats the JSON layer. Covers the ordering glue that lives only in Program.Main.
/// </summary>
public sealed class ConfigSourcePrecedenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-prec-" + Guid.NewGuid().ToString("N"));

    public ConfigSourcePrecedenceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Env_beats_stored_setting_beats_json()
    {
        var factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "settings.db") });
        new DatabaseMigrator(factory).Migrate();
        var settings = new SettingsRepository(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await settings.SetAsync("llm.live", "false", now, CancellationToken.None);          // → Llm:Live
        await settings.SetAsync("model.corner", "settings-model", now, CancellationToken.None); // → Llm:Routes:corner:ModelId

        const string envKey = "SIMCOACH_Llm__Live";
        Environment.SetEnvironmentVariable(envKey, "true");
        try
        {
            var builder = new ConfigurationBuilder();
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Live"] = "false",                        // json layer (lowest)
                ["Llm:Routes:corner:ModelId"] = "json-model",  // json layer
            });
            builder.AddEnvironmentVariables(prefix: "SIMCOACH_"); // env source must be last, as in Program
            TelemetryComposition.InsertSourceBelowLast(builder, new SqliteSettingsConfigurationSource(factory));
            IConfigurationRoot config = builder.Build();

            config["Llm:Live"].Should().Be("true", "a deliberate SIMCOACH_ env override wins over a stored row");
            config["Llm:Routes:corner:ModelId"].Should().Be("settings-model", "a stored row wins over JSON when env is silent");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
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
