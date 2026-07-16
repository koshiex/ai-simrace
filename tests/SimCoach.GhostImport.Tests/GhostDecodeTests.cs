using FluentAssertions;
using SimCoach.GhostImport;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Decode-path tests. See <see cref="SyntheticGhostFixture"/>'s header (M8): these prove
/// decoder-inverts-encoder self-consistency, NOT ACC format correctness.
/// </summary>
public sealed class GhostDecodeTests
{
    // A Monza-shaped world box (SHM carCoordinates ranges), used for the bbox guard.
    private static readonly GhostBbox _monzaBox = new(MinX: -398f, MaxX: 858f, MinZ: -1126f, MaxZ: 1045f);

    private static IReadOnlyList<GhostRecord> SampleRecords() =>
    [
        new GhostRecord(WorldX: 100.5f, WorldY: -5.0f, WorldZ: 200.25f, Yaw: 0.5f,
            BrakeNorm: 0f, ThrottleNorm: 1f, RawTimestamp: 0.0f),
        new GhostRecord(WorldX: -50.0f, WorldY: -4.5f, WorldZ: -300.75f, Yaw: -1.25f,
            BrakeNorm: 1f, ThrottleNorm: 0f, RawTimestamp: 1.5f),
        new GhostRecord(WorldX: 800.0f, WorldY: -6.0f, WorldZ: 1000.0f, Yaw: 3.0f,
            BrakeNorm: 0f, ThrottleNorm: 1f, RawTimestamp: 2.75f),
    ];

    [Fact]
    public void Multi_chunk_container_inflates_and_concatenates_to_the_payload()
    {
        byte[] payload = SyntheticGhostFixture.BuildPayload("monza", SampleRecords());
        byte[] file = SyntheticGhostFixture.BuildContainer(payload, chunkCount: 3);

        byte[] inflated = GhostContainer.Inflate(file);

        inflated.Should().Equal(payload);
    }

    [Fact]
    public void Payload_header_yields_the_track_id_and_record_count()
    {
        IReadOnlyList<GhostRecord> records = SampleRecords();
        byte[] payload = GhostContainer.Inflate(SyntheticGhostFixture.BuildGhost("monza", records));

        GhostPayloadHeader header = GhostPayload.ReadHeader(payload);

        header.TrackId.Should().Be("monza");
        header.RecordCount.Should().Be(records.Count);
        header.PayloadLength.Should().Be(payload.Length);
    }

    [Fact]
    public void Records_decode_world_xz_yaw_and_pedals()
    {
        IReadOnlyList<GhostRecord> expected = SampleRecords();
        byte[] payload = GhostContainer.Inflate(SyntheticGhostFixture.BuildGhost("monza", expected));
        GhostPayloadHeader header = GhostPayload.ReadHeader(payload);

        IReadOnlyList<GhostRecord> decoded = GhostPayload.ReadRecords(payload, header);

        decoded.Should().HaveCount(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            decoded[i].WorldX.Should().Be(expected[i].WorldX);
            decoded[i].WorldZ.Should().Be(expected[i].WorldZ);
            decoded[i].Yaw.Should().Be(expected[i].Yaw);
            decoded[i].BrakeNorm.Should().Be(expected[i].BrakeNorm);
            decoded[i].ThrottleNorm.Should().Be(expected[i].ThrottleNorm);
        }
    }

    [Fact]
    public void Arithmetic_guard_passes_for_a_well_formed_payload()
    {
        byte[] payload = GhostContainer.Inflate(SyntheticGhostFixture.BuildGhost("monza", SampleRecords()));
        GhostPayloadHeader header = GhostPayload.ReadHeader(payload);

        Action check = () => ImportGuards.CheckArithmetic(header);

        check.Should().NotThrow();
    }

    [Fact]
    public void Arithmetic_guard_throws_when_record_count_does_not_close_the_payload()
    {
        byte[] payload = GhostContainer.Inflate(SyntheticGhostFixture.BuildGhost("monza", SampleRecords()));
        GhostPayloadHeader wellFormed = GhostPayload.ReadHeader(payload);
        GhostPayloadHeader tampered = wellFormed with { RecordCount = wellFormed.RecordCount + 1 };

        Action check = () => ImportGuards.CheckArithmetic(tampered);

        check.Should().Throw<InvalidDataException>().WithMessage("*arithmetic mismatch*");
    }

    [Fact]
    public void Bbox_guard_passes_when_every_record_is_inside_the_track_box()
    {
        byte[] payload = GhostContainer.Inflate(SyntheticGhostFixture.BuildGhost("monza", SampleRecords()));
        GhostPayloadHeader header = GhostPayload.ReadHeader(payload);
        IReadOnlyList<GhostRecord> records = GhostPayload.ReadRecords(payload, header);

        Action check = () => ImportGuards.CheckWorldBbox(records, _monzaBox);

        check.Should().NotThrow();
    }

    [Fact]
    public void Bbox_guard_throws_when_a_record_falls_outside_the_track_box()
    {
        List<GhostRecord> records = [.. SampleRecords()];
        records.Add(new GhostRecord(WorldX: 5000f, WorldY: -5f, WorldZ: 0f, Yaw: 0f,
            BrakeNorm: 0f, ThrottleNorm: 1f, RawTimestamp: 3f));

        Action check = () => ImportGuards.CheckWorldBbox(records, _monzaBox);

        check.Should().Throw<InvalidDataException>().WithMessage("*outside*");
    }

    [Fact]
    public void Container_rejects_a_chunk_with_a_bad_magic()
    {
        byte[] file = SyntheticGhostFixture.BuildGhost("monza", SampleRecords(), chunkCount: 1);
        file[0] ^= 0xFF; // corrupt the container magic

        Action inflate = () => GhostContainer.Inflate(file);

        inflate.Should().Throw<InvalidDataException>().WithMessage("*magic mismatch*");
    }
}
