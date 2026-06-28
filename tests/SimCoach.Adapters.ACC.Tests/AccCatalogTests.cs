using FluentAssertions;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Tests for the static car/track catalogs that compensate for data ACC's shared memory
/// does not expose (per-car steering lock, per-track lap length).
/// </summary>
public sealed class AccCatalogTests
{
    [Theory]
    [InlineData("ferrari_488_gt3", 480f)]
    [InlineData("audi_r8_lms_evo_ii", 720f)]
    [InlineData("porsche_992_gt3_r", 800f)]
    [InlineData("bmw_m4_gt3", 516f)]       // community-measured; Kunos doc's 540 is wrong
    [InlineData("honda_nsx_gt3_evo", 436f)] // value telemetry tools ship; doc's 620 is stale
    [InlineData("maserati_mc_gt4", 900f)]
    [InlineData("bmw_m2_cs_racing", 360f)]
    public void Known_cars_have_documented_steer_locks(string carId, float expectedLockDeg)
    {
        // Act
        float lockDeg = AccCarCatalog.GetSteerLockDeg(carId);

        // Assert
        lockDeg.Should().Be(expectedLockDeg);
    }

    [Fact]
    public void Unknown_car_falls_back_to_default_lock()
    {
        // Act
        float lockDeg = AccCarCatalog.GetSteerLockDeg("spaceship_gt1");

        // Assert
        lockDeg.Should().Be(AccCarCatalog.FallbackSteerLockDeg);
    }

    [Fact]
    public void Car_catalog_covers_the_full_acc_roster()
    {
        // Assert — 54 cars in ACC 1.10: GT3 (all years + EVOs), GT4, GT2, Cup, ST, CHL, TCX
        AccCarCatalog.KnownCarCount.Should().Be(54);
    }

    [Theory]
    [InlineData("ferrari_296_gt3", "gt3")]
    [InlineData("porsche_991ii_gt3_cup", "gtc")]  // contains "gt3" but is a Cup car
    [InlineData("chevrolet_camaro_gt4r", "gt4")]   // irregular suffix (no underscore)
    [InlineData("jaguar_g3", "gt3")]               // encodes no class in the id
    [InlineData("porsche_935", "gt2")]
    [InlineData("bmw_m2_cs_racing", "tcx")]
    [InlineData("audi_r8_lms_gt2", "gt2")]
    public void Known_cars_resolve_their_competition_class(string carId, string expectedClass)
    {
        bool known = AccCarCatalog.TryGetCarClass(carId, out string carClass);

        known.Should().BeTrue();
        carClass.Should().Be(expectedClass);
    }

    [Fact]
    public void Unknown_car_has_no_class()
    {
        bool known = AccCarCatalog.TryGetCarClass("spaceship_gt1", out string carClass);

        known.Should().BeFalse();
        carClass.Should().BeNull();
    }

    [Theory]
    [InlineData("spa", 7004f)]
    [InlineData("nurburgring_24h", 25378f)] // Nordschleife 24h layout — the longest
    [InlineData("paul_ricard", 5770f)]      // shared memory says "Paul_Ricard"; keys are normalized
    [InlineData("laguna_seca", 3602f)]
    [InlineData("mount_panorama", 6213f)]
    public void Known_tracks_have_lap_lengths(string trackId, float expectedLengthM)
    {
        // Act
        bool isKnown = AccTrackCatalog.TryGetLapLengthM(trackId, out float lengthM);

        // Assert
        isKnown.Should().BeTrue();
        lengthM.Should().Be(expectedLengthM);
    }

    [Fact]
    public void Unknown_track_reports_no_length()
    {
        // Act
        bool isKnown = AccTrackCatalog.TryGetLapLengthM("moon_ring", out float lengthM);

        // Assert
        isKnown.Should().BeFalse();
        lengthM.Should().Be(0f);
    }

    [Fact]
    public void Track_catalog_covers_the_full_acc_track_list()
    {
        // Assert — 25 tracks in ACC 1.10 (base + all DLC packs)
        AccTrackCatalog.KnownTrackCount.Should().Be(25);
    }
}
