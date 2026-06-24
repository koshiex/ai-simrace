using SimCoach.Contracts.V1;

namespace SimCoach.Storage;

/// <summary>
/// Resamples a lap's channels onto a fixed 1 m position grid (one sample per metre, indices
/// <c>0..ceil(lapLengthM)-1</c>), keyed on <c>normalized_car_position × lapLengthM</c> ascending.
/// Float channels are linearly interpolated; gear takes the nearer frame. <c>t_ms_from_lap_start</c>
/// is derived (it is not a native frame field) as the milliseconds since the lap's first frame, then
/// interpolated like any other channel. A position-based grid makes time-at-position deltas robust to
/// frame-rate jitter. Non-monotonic position (a pit/out/in detour) is rejected — the caller feeds only
/// clean, fully-bounded laps.
/// </summary>
public static class PositionResampler
{
    /// <summary>Tolerance for the monotonic-position guard, absorbing float noise but not a real backstep.</summary>
    private const float MonotonicEpsilon = 1e-4f;

    // clampNonMonotonic=false (default): a backward position step (pit/out/in detour) throws — the
    // strict mode the reference candidate (ResampleSelf) uses. true: the backstep is clamped to the
    // running max so a crash/spin lap still resamples into laps.parquet for review; it is is_clean=0
    // and never becomes a reference. See ADR-0013.
    public static ResampledLap Resample(
        IReadOnlyList<TelemetryFrame> lapFrames, float lapLengthM, bool clampNonMonotonic = false)
    {
        ArgumentNullException.ThrowIfNull(lapFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);
        if (lapFrames.Count < 2)
        {
            throw new ArgumentException("Resampling needs at least two frames.", nameof(lapFrames));
        }

        int n = lapFrames.Count;
        float[] pos = new float[n];
        float[] tMs = new float[n];
        var lapStart = lapFrames[0].T.ToDateTimeOffset();
        for (int i = 0; i < n; i++)
        {
            float raw = lapFrames[i].NormalizedCarPosition;
            if (i > 0 && raw < pos[i - 1] - MonotonicEpsilon && !clampNonMonotonic)
            {
                throw new ArgumentException(
                    $"Lap position is not monotonic at frame {i} ({raw} < {pos[i - 1]}); "
                    + "a pit/out/in lap cannot be resampled.",
                    nameof(lapFrames));
            }

            // Clamping keeps the grid monotonic by pinning a backward step to the running max.
            pos[i] = (clampNonMonotonic && i > 0) ? MathF.Max(raw, pos[i - 1]) : raw;
            tMs[i] = (float)(lapFrames[i].T.ToDateTimeOffset() - lapStart).TotalMilliseconds;
        }

        int gridLength = (int)MathF.Ceiling(lapLengthM);
        var grid = new GridArrays(gridLength);
        int j = 0;
        for (int k = 0; k < gridLength; k++)
        {
            float target = k / lapLengthM;
            while (j < n - 2 && pos[j + 1] < target)
            {
                j++;
            }

            float p0 = pos[j];
            float p1 = pos[j + 1];
            float frac = p1 > p0 ? Math.Clamp((target - p0) / (p1 - p0), 0f, 1f) : 0f;
            grid.Fill(k, target, tMs[j], tMs[j + 1], lapFrames[j], lapFrames[j + 1], frac);
        }

        return grid.ToResampledLap(lapFrames[0].LapNumber, gridLength);
    }

    private static float Lerp(float a, float b, float frac) => a + ((b - a) * frac);

    private static float TyreTemp(TelemetryFrame frame, int wheel) =>
        frame.TyreTempC.Count > wheel ? frame.TyreTempC[wheel] : 0f;

    /// <summary>Mutable column buffers filled grid point by grid point, then frozen into a record.</summary>
    private sealed class GridArrays
    {
        private readonly float[] _position;
        private readonly int[] _tMs;
        private readonly float[] _speed;
        private readonly float[] _throttle;
        private readonly float[] _brake;
        private readonly float[] _steer;
        private readonly int[] _gear;
        private readonly float[] _tyreFl;
        private readonly float[] _tyreFr;
        private readonly float[] _tyreRl;
        private readonly float[] _tyreRr;
        private readonly float[] _gLat;
        private readonly float[] _gLong;
        private readonly float[] _worldX;
        private readonly float[] _worldY;
        private readonly float[] _worldZ;

        public GridArrays(int length)
        {
            _position = new float[length];
            _tMs = new int[length];
            _speed = new float[length];
            _throttle = new float[length];
            _brake = new float[length];
            _steer = new float[length];
            _gear = new int[length];
            _tyreFl = new float[length];
            _tyreFr = new float[length];
            _tyreRl = new float[length];
            _tyreRr = new float[length];
            _gLat = new float[length];
            _gLong = new float[length];
            _worldX = new float[length];
            _worldY = new float[length];
            _worldZ = new float[length];
        }

        public void Fill(
            int k, float target, float tMs0, float tMs1, TelemetryFrame f0, TelemetryFrame f1, float frac)
        {
            _position[k] = target;
            _tMs[k] = (int)MathF.Round(Lerp(tMs0, tMs1, frac));
            _speed[k] = Lerp(f0.SpeedMps, f1.SpeedMps, frac);
            _throttle[k] = Lerp(f0.ThrottlePct, f1.ThrottlePct, frac);
            _brake[k] = Lerp(f0.BrakePct, f1.BrakePct, frac);
            _steer[k] = Lerp(f0.SteerRad, f1.SteerRad, frac);
            _gear[k] = frac < 0.5f ? f0.Gear : f1.Gear;
            _tyreFl[k] = Lerp(TyreTemp(f0, 0), TyreTemp(f1, 0), frac);
            _tyreFr[k] = Lerp(TyreTemp(f0, 1), TyreTemp(f1, 1), frac);
            _tyreRl[k] = Lerp(TyreTemp(f0, 2), TyreTemp(f1, 2), frac);
            _tyreRr[k] = Lerp(TyreTemp(f0, 3), TyreTemp(f1, 3), frac);
            _gLat[k] = Lerp(f0.GForceG?.X ?? 0f, f1.GForceG?.X ?? 0f, frac);
            _gLong[k] = Lerp(f0.GForceG?.Z ?? 0f, f1.GForceG?.Z ?? 0f, frac);
            _worldX[k] = Lerp(f0.WorldPos?.X ?? 0f, f1.WorldPos?.X ?? 0f, frac);
            _worldY[k] = Lerp(f0.WorldPos?.Y ?? 0f, f1.WorldPos?.Y ?? 0f, frac);
            _worldZ[k] = Lerp(f0.WorldPos?.Z ?? 0f, f1.WorldPos?.Z ?? 0f, frac);
        }

        public ResampledLap ToResampledLap(int lapNumber, int gridLength) => new()
        {
            LapNumber = lapNumber,
            GridLength = gridLength,
            PositionNormalized = _position,
            TMsFromLapStart = _tMs,
            SpeedMps = _speed,
            ThrottlePct = _throttle,
            BrakePct = _brake,
            SteerRad = _steer,
            Gear = _gear,
            TyreTempFl = _tyreFl,
            TyreTempFr = _tyreFr,
            TyreTempRl = _tyreRl,
            TyreTempRr = _tyreRr,
            GLat = _gLat,
            GLong = _gLong,
            WorldX = _worldX,
            WorldY = _worldY,
            WorldZ = _worldZ,
        };
    }
}
