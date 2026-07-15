using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Pure (no IO) builder of the own-optimal reference (M46): given the stored clean-lap per-sector
/// distributions for a triple and the triple's PB lap time, selects the best clean duration per sector
/// (after guards) and sums them into a target lap time faster than any single lap driven.
///
/// Guards, in order:
///  (i)   a PB reference must exist — the optimal is only meaningful as a gain over PB;
///  (ii)  the gain (PB − Σ best sectors) must clear <see cref="OptimalReferenceOptions.MinOptimalGainMs"/>,
///        else PB already is the target and no optimal is written;
///  (iii) a per-sector OUTLIER guard rejects a candidate best sitting implausibly far below that sector's
///        clean-time distribution (tow / undetected cut / grip spike), independent of lap age.
/// A cheap per-lap <c>Σ sectors ≈ lap_time</c> filter drops timing-glitched laps before any distribution.
/// Deterministic: identical input yields identical output, so a rebuild is idempotent.
/// </summary>
public static class OptimalReferenceBuilder
{
    private const double MadToStddev = 1.4826; // MAD → robust stddev for a normal distribution.

    private readonly record struct SectorSample(int DurationMs, string SessionId, int LapNumber);

    /// <summary>
    /// Builds the optimal reference, or <c>null</c> when a guard blocks it (no PB, no usable clean laps,
    /// gain below the floor, or a sector with no non-outlier candidate).
    /// </summary>
    public static OptimalReference? Build(
        IReadOnlyList<CleanLapSectors> cleanLaps,
        int? pbLapTimeMs,
        OptimalReferenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(cleanLaps);
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        // Guard (i): no PB → no optimal (the target is expressed as a gain over the PB lap).
        if (pbLapTimeMs is not int pbMs)
        {
            return null;
        }

        List<CleanLapSectors> sane =
            [.. cleanLaps.Where(lap => IsSaneLap(lap, options.LapSumToleranceMs))];
        if (sane.Count == 0)
        {
            return null;
        }

        // Sector count from the data: the modal count, keeping only laps that agree (a lap with a
        // different number of sectors cannot contribute to every sector's distribution).
        int sectorCount = sane
            .GroupBy(lap => lap.SectorTimesMs.Count)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .First().Key;
        if (sectorCount == 0)
        {
            return null;
        }

        List<CleanLapSectors> usable = [.. sane.Where(lap => lap.SectorTimesMs.Count == sectorCount)];

        List<int> bestDurations = [];
        List<SectorBestSource> sources = [];
        for (int sector = 0; sector < sectorCount; sector++)
        {
            List<SectorSample> distribution =
                [.. usable.Select(lap => new SectorSample(lap.SectorTimesMs[sector], lap.SessionId, lap.LapNumber))];

            SectorSample? best = SelectGuardedBest(distribution, options);
            if (best is not SectorSample chosen)
            {
                return null;
            }

            bestDurations.Add(chosen.DurationMs);
            sources.Add(new SectorBestSource
            {
                SectorIndex = sector,
                DurationMs = chosen.DurationMs,
                SessionId = chosen.SessionId,
                LapNumber = chosen.LapNumber,
            });
        }

        int target = bestDurations.Sum();

        // Guard (ii): PB already is the target unless the stitched lap beats it by the configured floor.
        if (pbMs - target < options.MinOptimalGainMs)
        {
            return null;
        }

        return new OptimalReference
        {
            SectorDurationsMs = bestDurations,
            TargetLapTimeMs = target,
            Sources = sources,
        };
    }

    // A lap is sane when every sector is positive and Σ sectors is within tolerance of the recorded lap
    // time — a cheap cut/timing-glitch filter that runs before any sector enters a distribution.
    private static bool IsSaneLap(CleanLapSectors lap, int lapSumToleranceMs)
    {
        if (lap.SectorTimesMs.Count == 0 || lap.SectorTimesMs.Any(ms => ms <= 0))
        {
            return false;
        }

        int sum = 0;
        foreach (int ms in lap.SectorTimesMs)
        {
            sum += ms;
        }

        return Math.Abs(sum - lap.LapTimeMs) <= lapSumToleranceMs;
    }

    // Guard (iii): the smallest duration that is NOT an implausible outlier below the distribution. A
    // candidate is an outlier when it sits below median − max(MaxSectorOutlierMs, k × robustStddev). The
    // median is always a non-outlier, so a candidate always exists for a non-empty distribution.
    private static SectorSample? SelectGuardedBest(
        IReadOnlyList<SectorSample> distribution,
        OptimalReferenceOptions options)
    {
        if (distribution.Count == 0)
        {
            return null;
        }

        int[] durations = [.. distribution.Select(sample => sample.DurationMs).Order()];
        double median = Median(durations);
        double robustStddev = RobustStddev(durations, median);
        double allowedBelow = Math.Max(options.MaxSectorOutlierMs, options.OutlierRobustStddevMultiple * robustStddev);
        double lowerBound = median - allowedBelow;

        SectorSample? best = null;
        foreach (SectorSample sample in distribution)
        {
            if (sample.DurationMs < lowerBound)
            {
                continue;
            }

            if (best is not SectorSample current || sample.DurationMs < current.DurationMs)
            {
                best = sample;
            }
        }

        return best;
    }

    private static double Median(int[] ascending)
    {
        int mid = ascending.Length / 2;
        return ascending.Length % 2 == 1
            ? ascending[mid]
            : (ascending[mid - 1] + ascending[mid]) / 2.0;
    }

    private static double RobustStddev(int[] ascending, double median)
    {
        int[] deviations = [.. ascending.Select(value => (int)Math.Abs(value - median)).Order()];
        double mad = Median(deviations);
        return mad * MadToStddev;
    }
}
