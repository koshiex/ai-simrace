namespace SimCoach.Adapters.ACC;

/// <summary>Outcome of a single acquisition tick.</summary>
public enum AccAcquisitionStatus
{
    /// <summary>No new physics packet since the last tick (or the copy stayed torn this tick).</summary>
    NoNewFrame,

    /// <summary>A new coherent snapshot was produced.</summary>
    NewFrame,

    /// <summary>The page source failed; the caller should reconnect.</summary>
    Disconnected,
}
