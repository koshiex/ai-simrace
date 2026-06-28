namespace SimCoach.Coach.Actions;

/// <summary>
/// The read-only seam the clause evaluator, renderer, and valid-subset filter read a Gold artifact through.
/// It exposes a <b>flat</b> projection of the per-cadence Gold field space (nested groups flatten to flat,
/// collision-free keys). PR-C ships <see cref="DictionaryGoldView"/>; the typed Gold records from a later PR
/// are surfaced via an adapter implementing this interface — the records themselves grow no string indexer.
/// A missing field returns <c>false</c> so the evaluator can treat it as "not satisfied".
/// </summary>
public interface IGoldView
{
    CoachCadence Cadence { get; }

    bool HasReference { get; }

    bool TryGetNumber(string field, out double value);

    bool TryGetBool(string field, out bool value);

    bool TryGetString(string field, out string value);
}
