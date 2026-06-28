namespace SimCoach.Coach.Gold;

/// <summary>
/// The per-cadence session header on every <see cref="GoldArtifact{TEvent}"/>. <see cref="CarClass"/> is the
/// coarse class supplied as caller context (sourced by the sim adapter) — the raw <c>car_id</c> is NEVER carried
/// (exact car id must not leave the machine). <see cref="Weather"/> is the already-coarse <c>weather_bucket</c>
/// passed through verbatim. <see cref="LapNumber"/> is absent at session cadence. <see cref="HasReference"/> is
/// always serialized.
/// </summary>
public sealed record GoldSessionBlock(
    string TrackId,
    string CarClass,
    string Weather,
    int? LapNumber,
    bool HasReference);
