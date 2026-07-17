using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CenterlineGeometryDatasetTests
{
    [Fact]
    public void Loads_the_embedded_monza_centerline()
    {
        // Guards the csproj EmbeddedResource glob + the vendored asset: without them Load() is empty and the
        // whole M38 runtime LINE reference is silently inert (falls back to PB for every corner).
        var dataset = CenterlineGeometryDataset.Load();

        dataset.TryGetCenterline("monza", 5793f, out MedianCenterline? centerline).Should().BeTrue();
        centerline.Should().NotBeNull();
        centerline!.Bins.Should().NotBeEmpty();
        centerline.LapCount.Should().BeGreaterThanOrEqualTo(MedianCenterlineBuilder.MinLapsForTrust);
    }

    [Fact]
    public void Loads_the_embedded_spa_centerline()
    {
        // Regression guard for the culture-inference trap: "spa" is a valid culture code, so without the csproj
        // LogicalName/WithCulture pin MSBuild's AssignCulture strips centerline.spa.json into a phantom satellite
        // and Load() silently loses it — the whole Spa M38 LINE reference goes inert.
        var dataset = CenterlineGeometryDataset.Load();

        dataset.TryGetCenterline("spa", 7004f, out MedianCenterline? centerline).Should().BeTrue();
        centerline.Should().NotBeNull();
        centerline!.Bins.Should().NotBeEmpty();
        centerline.LapCount.Should().BeGreaterThanOrEqualTo(MedianCenterlineBuilder.MinLapsForTrust);
    }

    [Fact]
    public void Resolves_a_trustworthy_centerline()
    {
        var dataset = CenterlineGeometryDataset.FromDocuments([Document("monza", 1000f, lapCount: 4)]);

        dataset.TryGetCenterline("monza", 1000f, out MedianCenterline? centerline).Should().BeTrue();
        centerline.Should().NotBeNull();
        centerline!.Bins.Should().HaveCount(2);
    }

    [Fact]
    public void Rejects_an_unknown_track()
    {
        var dataset = CenterlineGeometryDataset.FromDocuments([Document("monza", 1000f, lapCount: 4)]);

        dataset.TryGetCenterline("spa", 1000f, out MedianCenterline? centerline).Should().BeFalse();
        centerline.Should().BeNull();
    }

    [Fact]
    public void Rejects_a_lap_length_mismatch()
    {
        var dataset = CenterlineGeometryDataset.FromDocuments([Document("monza", 1000f, lapCount: 4)]);

        dataset.TryGetCenterline("monza", 9999f, out _).Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_untrustworthy_centerline_below_the_lap_threshold()
    {
        var dataset = CenterlineGeometryDataset.FromDocuments([Document("monza", 1000f, lapCount: 2)]);

        dataset.TryGetCenterline("monza", 1000f, out _).Should().BeFalse("below MinLapsForTrust it falls back to PB");
    }

    private static CenterlineGeometryDocument Document(string trackId, float lapLengthM, int lapCount) => new()
    {
        SchemaVersion = CenterlineGeometryDocument.CurrentSchemaVersion,
        TrackId = trackId,
        LapLengthM = lapLengthM,
        LapCount = lapCount,
        Bins =
        [
            new CenterlineBin { DistanceM = 0, X = 1f, Z = 2f, LateralG = 0.1f, LapSamples = lapCount },
            new CenterlineBin { DistanceM = 1, X = 1.1f, Z = 2.2f, LateralG = 0.8f, LapSamples = lapCount },
        ],
    };
}
