using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class ReferenceTripleTests
{
    private static readonly ReferenceTriple _triple = new("monza", "bmw_m4_gt3", "dry-warm");

    [Fact]
    public void ParquetFileNameFor_encodes_the_kind_string()
    {
        _triple.ParquetFileNameFor(ReferenceKind.AlienLine)
            .Should().Be("monza_bmw_m4_gt3_dry-warm_alien_line.parquet");
    }

    [Fact]
    public void ParquetFileNameFor_distinguishes_alien_line_from_pb_on_the_same_triple()
    {
        _triple.ParquetFileNameFor(ReferenceKind.AlienLine)
            .Should().NotBe(_triple.ParquetFileNameFor(ReferenceKind.Pb));
    }

    [Fact]
    public void ParquetFileNameFor_pb_encodes_the_pb_kind()
    {
        _triple.ParquetFileNameFor(ReferenceKind.Pb).Should().Be("monza_bmw_m4_gt3_dry-warm_pb.parquet");
    }

    [Fact]
    public void ParquetFileNameFor_sanitizes_each_segment()
    {
        var triple = new ReferenceTriple("Monza GP", "Ferrari 296", "Dry/Warm");

        triple.ParquetFileNameFor(ReferenceKind.AlienLine)
            .Should().Be("monza_gp_ferrari_296_dry_warm_alien_line.parquet");
    }

    [Fact]
    public void ParquetFileNameFor_does_not_disturb_the_kind_less_property()
    {
        _triple.ParquetFileName.Should().Be("monza_bmw_m4_gt3_dry-warm.parquet");
    }
}
