using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SimCoach.App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "SIMCOACH_");

        builder.Services.AddSerilog((services, config) =>
            config.ReadFrom.Configuration(builder.Configuration));

        // TODO Phase 0+: register hosted services here as modules come online.

        using var host = builder.Build();
        await host.RunAsync();
        return 0;
    }
}
