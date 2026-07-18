using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Reference;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class TrackModelStoreTests
{
    [Fact]
    public void Resolves_the_baked_model_for_a_covered_track()
    {
        TrackModelStore store = NewStore(("monza", 5793f));

        TrackModel model = store.Get("monza");

        model.Source.Should().Be(TrackModelSource.Baked);
        model.Corners.Should().NotBeEmpty();
        model.Corners.Should().OnlyContain(c => c.Name == null);
    }

    [Fact]
    public void Returns_none_for_an_uncovered_track()
    {
        TrackModelStore store = NewStore(("test_oval", 2000f));

        TrackModel model = store.Get("test_oval");

        model.Source.Should().Be(TrackModelSource.None);
        model.Corners.Should().BeEmpty();
    }

    [Fact]
    public void Returns_none_when_the_lap_length_is_unknown()
    {
        TrackModelStore store = NewStore();

        store.Get("monza").Source.Should().Be(TrackModelSource.None);
    }

    private static TrackModelStore NewStore(params (string TrackId, float LengthM)[] lengths) =>
        new(CornerGeometryDataset.Load(), new FakeTrackLengths(lengths), NullLogger<TrackModelStore>.Instance);

    private sealed class FakeTrackLengths : ITrackLengthProvider
    {
        private readonly Dictionary<string, float> _lengths;

        public FakeTrackLengths((string TrackId, float LengthM)[] lengths) =>
            _lengths = lengths.ToDictionary(e => e.TrackId, e => e.LengthM, StringComparer.Ordinal);

        public bool TryGetLapLengthM(string trackId, out float lengthM) =>
            _lengths.TryGetValue(trackId, out lengthM);
    }
}
