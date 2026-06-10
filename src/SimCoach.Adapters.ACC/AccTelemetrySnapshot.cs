using SimCoach.Adapters.ACC.SharedMemory;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// A coherent capture of all three ACC shared-memory pages, taken when a new physics packet
/// appeared. Graphics and static pages may be slightly older than the physics page — they are
/// cached and refreshed on their own cadence (graphics per its packetId, static per interval).
/// <para>
/// <paramref name="CapturedAt"/> is wall-clock UTC for display/storage;
/// <paramref name="CapturedAtTimestamp"/> is the monotonic tick count from
/// <see cref="TimeProvider.GetTimestamp"/> — use it for frame ordering and durations
/// (wall clock can step backwards under NTP correction, monotonic time cannot).
/// </para>
/// </summary>
public sealed record AccTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    long CapturedAtTimestamp,
    AccPhysicsPage Physics,
    AccGraphicsPage Graphics,
    AccStaticPage Static);
