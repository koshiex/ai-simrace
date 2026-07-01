using FluentAssertions;
using Microsoft.Extensions.Options;
using SimCoach.Coach;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class RateCardQueryTests
{
    [Fact]
    public async Task PerLap_amortises_cadence_calls_over_typical_laps()
    {
        RateCardQuery query = Query();

        decimal perLap = await query.EstimatePerLapUsd("google/gemini-2.5-flash-lite", CoachCadence.Corner, CancellationToken.None);

        // perCall = 700/1e6*0.1 + 24/1e6*0.4 = 0.0000796 ; perLap = perCall*100/20
        perLap.Should().BeApproximately(0.0000796m * 100 / 20, 1e-12m);
    }

    [Fact]
    public async Task PerSession_sums_every_cadence_call_volume()
    {
        RateCardQuery query = Query();

        decimal perSession = await query.EstimatePerSessionUsd("google/gemini-2.5-flash-lite", CancellationToken.None);

        // corner perCall*100 + session perCall*1
        decimal cornerPerCall = (700 / 1_000_000m * 0.1m) + (24 / 1_000_000m * 0.4m);
        decimal sessionPerCall = (4000 / 1_000_000m * 0.1m) + (600 / 1_000_000m * 0.4m);
        perSession.Should().BeApproximately((cornerPerCall * 100) + (sessionPerCall * 1), 1e-12m);
    }

    [Fact]
    public async Task Unknown_model_throws()
    {
        RateCardQuery query = Query();

        Func<Task> act = () => query.EstimatePerSessionUsd("unknown/model", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static RateCardQuery Query()
    {
        IOptions<LlmOptions> llm = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openrouter-google"] = new()
                {
                    BaseUrl = "https://openrouter.test/api/v1/",
                    AuthEnvVar = "OPENROUTER_API_KEY",
                    Rates = new Dictionary<string, ModelRate>
                    {
                        ["google/gemini-2.5-flash-lite"] = new()
                        {
                            InputPerMillion = 0.1m,
                            OutputPerMillion = 0.4m,
                        },
                    },
                },
            },
        });

        IOptions<RateCardOptions> card = Options.Create(new RateCardOptions
        {
            TypicalLapsPerSession = 20,
            Cadences = new Dictionary<CoachCadence, CadenceEstimate>
            {
                [CoachCadence.Corner] = new(700, 24, 100),
                [CoachCadence.Session] = new(4000, 600, 1),
            },
        });

        return new RateCardQuery(llm, card);
    }
}
