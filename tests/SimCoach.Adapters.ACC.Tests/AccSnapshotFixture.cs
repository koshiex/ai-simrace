using SimCoach.Adapters.ACC.SharedMemory;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Builds <see cref="AccTelemetrySnapshot"/> instances for mapper tests by marshaling synthetic
/// pages — the same path production snapshots take, so arrays are always non-null.
/// </summary>
internal static class AccSnapshotFixture
{
    public static readonly DateTimeOffset CapturedAt = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    public static AccTelemetrySnapshot Build(
        Action<PageFixtureBuilder>? physics = null,
        Action<PageFixtureBuilder>? graphics = null,
        Action<PageFixtureBuilder>? @static = null)
    {
        PageFixtureBuilder physicsBuilder = new(AccPhysicsPage.SizeBytes);
        physics?.Invoke(physicsBuilder);
        PageFixtureBuilder graphicsBuilder = new(AccGraphicsPage.SizeBytes);
        graphics?.Invoke(graphicsBuilder);
        PageFixtureBuilder staticBuilder = new(AccStaticPage.SizeBytes);
        @static?.Invoke(staticBuilder);

        return new AccTelemetrySnapshot(
            CapturedAt,
            CapturedAt.Ticks,
            AccPageMarshaller.Read<AccPhysicsPage>(physicsBuilder.Build()),
            AccPageMarshaller.Read<AccGraphicsPage>(graphicsBuilder.Build()),
            AccPageMarshaller.Read<AccStaticPage>(staticBuilder.Build()));
    }
}
