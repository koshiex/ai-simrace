using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Gold;

/// <summary>
/// Single entry point that wraps a built Gold artifact in its typed <see cref="IGoldView"/> (the seam the rule
/// engine / valid-subset filter read through). Only the three registry-active cadences have an adapter; a
/// session/strategy payload throws (no actions exist there).
/// </summary>
public static class GoldView
{
    public static IGoldView For<TEvent>(GoldArtifact<TEvent> artifact) => artifact switch
    {
        GoldArtifact<GoldCornerEvent> corner => new CornerGoldView(corner),
        GoldArtifact<GoldSectorEvent> sector => new SectorGoldView(sector),
        GoldArtifact<GoldLapEvent> lap => new LapGoldView(lap),
        _ => throw new NotSupportedException(
            $"No IGoldView adapter for Gold payload '{typeof(TEvent).Name}'."),
    };
}
