namespace SimCoach.Coach.Gold;

/// <summary>
/// Deterministic decimal rounding for every scalar that enters a Gold artifact. Proto telemetry fields are
/// <c>float</c>; widened to <c>double</c> they carry long binary tails (<c>0.22f</c> → <c>0.2199…</c>), so the
/// builder rounds AFTER widening and the serializer then emits a stable short form. Every emitted double routes
/// through one of these — not just the corner scalars — or an unrounded tail ships green. Named decimal places
/// (no magic numbers); <see cref="MidpointRounding.AwayFromZero"/> so half-values are stable across runs.
/// </summary>
internal static class Rounding
{
    private const int MetersDecimals = 1;
    private const int KmhDecimals = 1;
    private const int CelsiusDecimals = 1;
    private const int StddevDecimals = 1;
    private const int ScoreDecimals = 2;
    private const int FuelDecimals = 2;
    private const int PercentDecimals = 2;

    public static double Meters(double value) => Round(value, MetersDecimals);

    public static double Kmh(double value) => Round(value, KmhDecimals);

    public static double Celsius(double value) => Round(value, CelsiusDecimals);

    /// <summary>A millisecond standard deviation (kept distinct from <see cref="Score"/> so the units don't drift).</summary>
    public static double Stddev(double value) => Round(value, StddevDecimals);

    /// <summary>A 0..1 derived score.</summary>
    public static double Score(double value) => Round(value, ScoreDecimals);

    public static double Fuel(double value) => Round(value, FuelDecimals);

    /// <summary>A 0..1 fraction (wear / degradation).</summary>
    public static double Percent(double value) => Round(value, PercentDecimals);

    private static double Round(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
