using SimCoach.Storage;

namespace SimCoach.Storage.Tests;

/// <summary>Test <see cref="ITrackLengthProvider"/> returning a fixed lap length for any track.</summary>
internal sealed class FakeTrackLengths : ITrackLengthProvider
{
    private readonly float _lengthM;

    public FakeTrackLengths(float lengthM = 7004f) => _lengthM = lengthM;

    public bool TryGetLapLengthM(string trackId, out float lengthM)
    {
        lengthM = _lengthM;
        return true;
    }
}
