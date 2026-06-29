using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Storage.Repositories;

namespace SimCoach.Coach;

/// <summary>
/// Composes the Coach ring: the concrete options its bare-options consumers need, the data singletons
/// (<see cref="ActionRegistry"/>/<see cref="CornerNameMap"/>), the Gold/prompt/rule builders, the live ambient
/// state (gate + session metadata feed), the tip sink, and the two hosted services. Depends on
/// <c>AddLlm</c> having run (it consumes <c>IOptions&lt;LlmOptions&gt;</c> and <see cref="ICostQueryRepository"/>)
/// and on the App-edge bridges (<c>ICarClassProvider</c>, <c>ISessionIdProvider</c>). Public so App.Tests can
/// build the same graph. The two <c>AddHostedService</c> calls here are ordered CoachService → LiveCoachAmbientState
/// and are slotted between the recorder and ComputeService by the composition root, so stop order holds.
/// </summary>
public static class CoachServiceCollectionExtensions
{
    public static IServiceCollection AddCoaching(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Concrete, eagerly-validated options for the bare-options consumers (CoachService, the builders, the
        // rule engine, the corner-phase resolver).
        CoachOptions coachOptions = configuration.GetSection("Coach").Get<CoachOptions>() ?? new CoachOptions();
        coachOptions.EnsureValid();
        services.AddSingleton(coachOptions);

        PromptOptions promptOptions =
            configuration.GetSection("Coach:Prompts").Get<PromptOptions>() ?? new PromptOptions();
        promptOptions.EnsureValid();
        services.AddSingleton(promptOptions);
        services.AddSingleton<IOptions<PromptOptions>>(Options.Create(promptOptions));

        RuleEngineOptions ruleOptions =
            configuration.GetSection("Coach:Rules").Get<RuleEngineOptions>() ?? new RuleEngineOptions();
        ruleOptions.EnsureValid();
        services.AddSingleton(ruleOptions);

        RateCardOptions rateCardOptions =
            configuration.GetSection("Coach:RateCard").Get<RateCardOptions>() ?? new RateCardOptions();
        rateCardOptions.EnsureValid();
        services.AddSingleton<IOptions<RateCardOptions>>(Options.Create(rateCardOptions));

        // Bind + ValidateOnStart fires CoachStartupValidator (#2 route/cadence, #4 registry-vs-Gold, #6 prompts)
        // at host start; the value it validates is the same section the concrete above is built from.
        services.AddOptions<CoachOptions>().Bind(configuration.GetSection("Coach")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CoachOptions>, CoachStartupValidator>();

        services.AddSingleton(ActionRegistry.Load());
        services.AddSingleton(CornerNameMap.Load());
        services.AddSingleton<GoldArtifactBuilder>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<RuleEngine>();
        services.AddSingleton<CornerPhaseResolver>();
        services.AddSingleton<IRateCardQuery, RateCardQuery>();

        services.AddSingleton<CoachTipRepository>();
        services.AddSingleton<ICoachTipSink, ConsoleTipSink>();

        // CoachService stops after ComputeService completes the domain fan-out (it drains it to completion);
        // registered before the ambient state so the ambient stops first. Both slot between the recorder and
        // ComputeService at the composition root.
        services.AddHostedService<CoachService>();

        services.AddSingleton<LiveCoachAmbientState>();
        services.AddSingleton<ICoachAmbientState>(static sp => sp.GetRequiredService<LiveCoachAmbientState>());
        services.AddHostedService(static sp => sp.GetRequiredService<LiveCoachAmbientState>());

        return services;
    }
}
