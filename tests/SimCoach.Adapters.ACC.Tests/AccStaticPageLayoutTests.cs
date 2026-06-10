using System.Runtime.InteropServices;
using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Golden layout tests for <see cref="AccStaticPage"/> against the Kunos shared-memory
/// documentation V1.8.12 (SPageFileStatic, pack 4, 820 bytes). Several popular C# ports
/// declare trackConfiguration as wchar_t[15] instead of wchar_t[33], shifting everything
/// from ersMaxJ onward by 36 bytes — the ErsMaxJ/DryTyresName/WetTyresName asserts below
/// guard against reintroducing that bug.
/// </summary>
public sealed class AccStaticPageLayoutTests
{
    private const int DocumentedSizeBytes = 820;

    [Fact]
    public void Static_page_size_matches_documented_native_layout()
    {
        // Act
        int marshaledSize = Marshal.SizeOf<AccStaticPage>();

        // Assert
        marshaledSize.Should().Be(DocumentedSizeBytes);
        AccStaticPage.SizeBytes.Should().Be(DocumentedSizeBytes);
    }

    [Theory]
    [InlineData(nameof(AccStaticPage.SmVersion), 0)]
    [InlineData(nameof(AccStaticPage.AcVersion), 30)]
    [InlineData(nameof(AccStaticPage.NumberOfSessions), 60)]
    [InlineData(nameof(AccStaticPage.NumCars), 64)]
    [InlineData(nameof(AccStaticPage.CarModel), 68)]
    [InlineData(nameof(AccStaticPage.Track), 134)]
    [InlineData(nameof(AccStaticPage.PlayerName), 200)]
    [InlineData(nameof(AccStaticPage.PlayerSurname), 266)]
    [InlineData(nameof(AccStaticPage.PlayerNick), 332)]
    [InlineData(nameof(AccStaticPage.SectorCount), 400)] // 2-byte pad after playerNick[33]
    [InlineData(nameof(AccStaticPage.MaxTorque), 404)]
    [InlineData(nameof(AccStaticPage.MaxPower), 408)]
    [InlineData(nameof(AccStaticPage.MaxRpm), 412)]
    [InlineData(nameof(AccStaticPage.MaxFuel), 416)]
    [InlineData(nameof(AccStaticPage.SuspensionMaxTravel), 420)]
    [InlineData(nameof(AccStaticPage.TyreRadius), 436)]
    [InlineData(nameof(AccStaticPage.MaxTurboBoost), 452)]
    [InlineData(nameof(AccStaticPage.Deprecated1), 456)]
    [InlineData(nameof(AccStaticPage.Deprecated2), 460)]
    [InlineData(nameof(AccStaticPage.PenaltiesEnabled), 464)]
    [InlineData(nameof(AccStaticPage.AidFuelRate), 468)]
    [InlineData(nameof(AccStaticPage.AidTireRate), 472)]
    [InlineData(nameof(AccStaticPage.AidMechanicalDamage), 476)]
    [InlineData(nameof(AccStaticPage.AidAllowTyreBlankets), 480)]
    [InlineData(nameof(AccStaticPage.AidStability), 484)]
    [InlineData(nameof(AccStaticPage.AidAutoClutch), 488)]
    [InlineData(nameof(AccStaticPage.AidAutoBlip), 492)]
    [InlineData(nameof(AccStaticPage.HasDrs), 496)]
    [InlineData(nameof(AccStaticPage.HasErs), 500)]
    [InlineData(nameof(AccStaticPage.HasKers), 504)]
    [InlineData(nameof(AccStaticPage.KersMaxJ), 508)]
    [InlineData(nameof(AccStaticPage.EngineBrakeSettingsCount), 512)]
    [InlineData(nameof(AccStaticPage.ErsPowerControllerCount), 516)]
    [InlineData(nameof(AccStaticPage.TrackSplineLength), 520)]
    [InlineData(nameof(AccStaticPage.TrackConfiguration), 524)] // wchar_t[33] — NOT [15]
    [InlineData(nameof(AccStaticPage.ErsMaxJ), 592)] // 2-byte pad after trackConfiguration[33]
    [InlineData(nameof(AccStaticPage.IsTimedRace), 596)]
    [InlineData(nameof(AccStaticPage.HasExtraLap), 600)]
    [InlineData(nameof(AccStaticPage.CarSkin), 604)]
    [InlineData(nameof(AccStaticPage.ReversedGridPositions), 672)] // 2-byte pad after carSkin[33]
    [InlineData(nameof(AccStaticPage.PitWindowStart), 676)]
    [InlineData(nameof(AccStaticPage.PitWindowEnd), 680)]
    [InlineData(nameof(AccStaticPage.IsOnline), 684)]
    [InlineData(nameof(AccStaticPage.DryTyresName), 688)]
    [InlineData(nameof(AccStaticPage.WetTyresName), 754)]
    public void Static_field_offset_matches_documented_native_layout(string fieldName, int expectedOffset)
    {
        // Act
        int actualOffset = Marshal.OffsetOf<AccStaticPage>(fieldName).ToInt32();

        // Assert
        actualOffset.Should().Be(expectedOffset, $"native SPageFileStatic places {fieldName} at byte {expectedOffset}");
    }

    [Fact]
    public void Static_fixture_page_parses_known_values_at_documented_offsets()
    {
        // Arrange
        byte[] page = new PageFixtureBuilder(DocumentedSizeBytes)
            .WithUtf16(0, "1.8", 15)                // smVersion
            .WithUtf16(30, "1.9.6", 15)             // acVersion
            .WithInt32(64, 30)                      // numCars
            .WithUtf16(68, "audi_r8_lms_evo_ii", 33) // carModel
            .WithUtf16(134, "Spa", 33)              // track
            .WithInt32(400, 3)                      // sectorCount
            .WithInt32(412, 8650)                   // maxRpm
            .WithInt32(676, -1)                     // pitWindowStart (no mandatory pit)
            .WithInt32(684, 1)                      // isOnline
            .WithUtf16(688, "DHE", 33)              // dryTyresName
            .WithUtf16(754, "WH", 33)               // wetTyresName — last field, checks tail alignment
            .Build();

        // Act
        AccStaticPage parsed = AccPageMarshaller.Read<AccStaticPage>(page);

        // Assert
        parsed.SmVersion.Should().Be("1.8");
        parsed.AcVersion.Should().Be("1.9.6");
        parsed.NumCars.Should().Be(30);
        parsed.CarModel.Should().Be("audi_r8_lms_evo_ii");
        parsed.Track.Should().Be("Spa");
        parsed.SectorCount.Should().Be(3);
        parsed.MaxRpm.Should().Be(8650);
        parsed.PitWindowStart.Should().Be(-1);
        parsed.IsOnline.Should().Be(1);
        parsed.DryTyresName.Should().Be("DHE");
        parsed.WetTyresName.Should().Be("WH");
    }

    [Fact]
    public void Static_string_at_maximum_terminated_length_roundtrips_without_bleeding()
    {
        // Arrange — 32 chars + implicit terminator fills wchar_t[33] exactly
        string longestTrack = new('t', 32);
        byte[] page = new PageFixtureBuilder(DocumentedSizeBytes)
            .WithUtf16(134, longestTrack, 33)   // track
            .WithUtf16(200, "Max", 33)          // playerName — adjacent field must stay untouched
            .Build();

        // Act
        AccStaticPage parsed = AccPageMarshaller.Read<AccStaticPage>(page);

        // Assert
        parsed.Track.Should().Be(longestTrack);
        parsed.PlayerName.Should().Be("Max");
    }
}
