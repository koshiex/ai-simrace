namespace SimCoach.Coach.Gold;

/// <summary>
/// The caller-supplied session context the per-cadence builders need but the corner/sector/lap proto events do
/// not carry (they have no lap number, session id, or session metadata). <see cref="CarClass"/> is the coarse,
/// privacy-safe class sourced by the sim adapter — never the raw <c>car_id</c>. <c>BuildSession</c> reads
/// track/weather/counts from the <c>SessionEvent</c> itself and uses only <see cref="CarClass"/>/
/// <see cref="HasReference"/>/<see cref="Locale"/> from here.
/// </summary>
public sealed record GoldSessionContext(
    string TrackId,
    string CarClass,
    string WeatherBucket,
    int LapNumber,
    bool HasReference,
    string Locale = "ru-RU");
