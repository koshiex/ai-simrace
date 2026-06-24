using SimCoach.Storage;

namespace SimCoach.Reference.Tests;

/// <summary>Test <see cref="ITrackLengthProvider"/>: known tracks return a length, others miss.</summary>
internal sealed class FakeTrackLengths : ITrackLengthProvider
{
    private readonly Dictionary<string, float> _lengths;

    public FakeTrackLengths(params (string TrackId, float LengthM)[] tracks) =>
        _lengths = tracks.ToDictionary(t => t.TrackId, t => t.LengthM, StringComparer.Ordinal);

    public static FakeTrackLengths Spa() => new(("spa", 7004f));

    public bool TryGetLapLengthM(string trackId, out float lengthM) =>
        _lengths.TryGetValue(trackId, out lengthM);
}
