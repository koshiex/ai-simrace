using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Pipeline.Segmentation;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class TrackModelStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolves_the_dataset_model_for_a_covered_track()
    {
        TrackModelStore store = NewStore(("spa", SyntheticTracks.Spa.LapLengthM));

        TrackModel model = store.Get("spa");

        model.Source.Should().Be(TrackModelSource.Dataset);
        model.Corners.Should().NotBeEmpty();
        model.Corners.Should().OnlyContain(c => c.Name != null);
    }

    [Fact]
    public void Falls_back_to_a_persisted_derived_model()
    {
        var repository = new JsonTrackModelRepository(_root);
        repository.Save(DerivedModel("test_oval", 90000));
        // No lap length registered → dataset path is skipped, the persisted derived model wins.
        TrackModelStore store = NewStore(repository);

        TrackModel model = store.Get("test_oval");

        model.Source.Should().Be(TrackModelSource.Derived);
        model.DerivedFromLapTimeMs.Should().Be(90000);
    }

    [Fact]
    public void Returns_none_when_uncovered_and_not_yet_derived()
    {
        TrackModelStore store = NewStore();

        TrackModel model = store.Get("unknown_track");

        model.Source.Should().Be(TrackModelSource.None);
        model.Corners.Should().BeEmpty();
    }

    [Fact]
    public void Derive_persists_and_only_rebuilds_on_a_faster_lap()
    {
        TrackModelStore store = NewStore();
        CompletedLap baseLap = TestLaps.FastestClean(SyntheticTracks.TestOval);

        TrackModel first = store.Derive("test_oval", TestLaps.WithLapTime(baseLap, 90000));
        first.DerivedFromLapTimeMs.Should().Be(90000);

        // A slower lap must not rebuild.
        TrackModel afterSlower = store.Derive("test_oval", TestLaps.WithLapTime(baseLap, 95000));
        afterSlower.DerivedFromLapTimeMs.Should().Be(90000);

        // A faster lap replaces it.
        TrackModel afterFaster = store.Derive("test_oval", TestLaps.WithLapTime(baseLap, 85000));
        afterFaster.DerivedFromLapTimeMs.Should().Be(85000);

        store.Get("test_oval").DerivedFromLapTimeMs.Should().Be(85000);
    }

    [Fact]
    public void Derive_does_not_rebuild_on_an_equal_lap_time()
    {
        TrackModelStore store = NewStore();
        CompletedLap baseLap = TestLaps.FastestClean(SyntheticTracks.TestOval);

        store.Derive("test_oval", TestLaps.WithLapTime(baseLap, 90000));
        // The guard is `stored <= candidate`, so an equal time keeps the stored model.
        TrackModel afterEqual = store.Derive("test_oval", TestLaps.WithLapTime(baseLap, 90000));

        afterEqual.DerivedFromLapTimeMs.Should().Be(90000);
        store.Get("test_oval").DerivedFromLapTimeMs.Should().Be(90000);
    }

    private TrackModelStore NewStore(params (string TrackId, float LengthM)[] lengths) =>
        NewStore(new JsonTrackModelRepository(_root), lengths);

    private static TrackModelStore NewStore(JsonTrackModelRepository repository, params (string TrackId, float LengthM)[] lengths) =>
        new(LandmarkDataset.Load(), repository, new FakeTrackLengths(lengths), NullLogger<TrackModelStore>.Instance);

    private static TrackModel DerivedModel(string trackId, int lapTimeMs) => new()
    {
        TrackId = trackId,
        Source = TrackModelSource.Derived,
        DerivedFromLapTimeMs = lapTimeMs,
        Corners = [new Corner { Id = $"{trackId}_t01", Name = null, StartPosition = 0.2f, ApexPosition = 0.25f, EndPosition = 0.35f }],
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeTrackLengths : ITrackLengthProvider
    {
        private readonly Dictionary<string, float> _lengths;

        public FakeTrackLengths((string TrackId, float LengthM)[] lengths) =>
            _lengths = lengths.ToDictionary(e => e.TrackId, e => e.LengthM, StringComparer.Ordinal);

        public bool TryGetLapLengthM(string trackId, out float lengthM) =>
            _lengths.TryGetValue(trackId, out lengthM);
    }
}
