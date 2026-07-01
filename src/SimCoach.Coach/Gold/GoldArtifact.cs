namespace SimCoach.Coach.Gold;

/// <summary>
/// The Gold-tier envelope serialized to the LLM, one per coaching cadence. <typeparamref name="TEvent"/> is the
/// concrete per-cadence payload (<see cref="GoldCornerEvent"/>/<see cref="GoldSectorEvent"/>/<see cref="GoldLapEvent"/>/
/// <see cref="GoldSessionPayload"/>); keeping it generic lets each <c>Build*</c> return a concretely-typed artifact
/// so the serializer emits exactly that shape with no polymorphic handling. Only derived Gold-tier scalars ride
/// here — never raw telemetry (privacy choke point, enforced by the serializer test).
/// </summary>
/// <param name="SchemaVersion">Always <c>"gold/1"</c>.</param>
/// <param name="Cadence">The cadence name: <c>"corner"</c>/<c>"sector"</c>/<c>"lap"</c>/<c>"session"</c> (NOT the
/// <c>"debrief"</c> route key, which is a separate concept).</param>
public sealed record GoldArtifact<TEvent>(
    string SchemaVersion,
    string Cadence,
    string Locale,
    GoldSessionBlock Session,
    TEvent Event);
