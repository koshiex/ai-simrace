using FluentAssertions;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class JsonTrackModelRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-trackmodels-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Get_returns_null_when_nothing_saved()
    {
        JsonTrackModelRepository repository = new(_root);

        repository.Get("spa").Should().BeNull();
    }

    [Fact]
    public void Save_then_get_round_trips_the_model()
    {
        JsonTrackModelRepository repository = new(_root);
        TrackModel model = new()
        {
            TrackId = "test_oval",
            Source = TrackModelSource.Derived,
            DerivedFromLapTimeMs = 91234,
            Corners =
            [
                new Corner { Id = "test_oval_t01", Name = null, StartPosition = 0.2f, ApexPosition = 0.25f, EndPosition = 0.35f },
                new Corner { Id = "test_oval_t02", Name = null, StartPosition = 0.7f, ApexPosition = 0.75f, EndPosition = 0.85f },
            ],
        };

        repository.Save(model);
        TrackModel? loaded = repository.Get("test_oval");

        loaded.Should().NotBeNull();
        loaded!.TrackId.Should().Be("test_oval");
        loaded.Source.Should().Be(TrackModelSource.Derived);
        loaded.DerivedFromLapTimeMs.Should().Be(91234);
        loaded.Corners.Should().Equal(model.Corners);
    }

    [Fact]
    public void Rejects_a_track_id_that_could_escape_the_directory()
    {
        JsonTrackModelRepository repository = new(_root);
        TrackModel model = new()
        {
            TrackId = "../escape",
            Source = TrackModelSource.Derived,
            Corners = [],
        };

        Action save = () => repository.Save(model);

        save.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
