using Microsoft.Extensions.Logging;
using SimCoach.Storage.Repositories;

namespace SimCoach.Coach;

/// <summary>
/// The P3 tip sink: emits a structured log line and persists the tip to the <c>coach_tips</c> log. The
/// short / spoken corner-name forms ride the <see cref="CoachTip"/> for the voice/overlay surfaces and are
/// intentionally not persisted here (the log/debrief re-render from the full name + rendered param).
/// </summary>
public sealed class ConsoleTipSink : ICoachTipSink
{
    private readonly CoachTipRepository _repository;
    private readonly ILogger<ConsoleTipSink> _logger;

    public ConsoleTipSink(CoachTipRepository repository, ILogger<ConsoleTipSink> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _logger = logger;
    }

    public Task EmitTipAsync(CoachTip tip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tip);

        _logger.LogInformation(
            "Coach tip [{Cadence}/{Severity}] {ActionId} \"{PhraseRu}\" (source={Source}, noPb={NoPbYet})",
            tip.Cadence, tip.Severity, tip.ActionId, tip.PhraseRu, tip.Source, tip.NoPbYet);

        _repository.Insert(ToRow(tip));
        return Task.CompletedTask;
    }

    private static CoachTipRow ToRow(CoachTip tip) => new()
    {
        SessionId = tip.SessionId,
        Cadence = tip.Cadence.ToString(),
        CornerId = tip.CornerId,
        LapNumber = tip.LapNumber,
        ActionId = tip.ActionId,
        ActionLabelShort = tip.ActionLabelShort,
        RenderedParam = tip.RenderedParam,
        PriorityPhase = tip.Priority.Phase.ToString(),
        PriorityRank = tip.Priority.Rank,
        Severity = tip.Severity.ToString(),
        PhraseRu = tip.PhraseRu,
        CornerName = tip.CornerName,
        Source = tip.Source.ToString(),
        NoPbYet = tip.NoPbYet,
        ProviderModelId = tip.ProviderModelId,
        GeneratedAtUtc = tip.GeneratedAtUtc,
    };
}
