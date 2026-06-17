using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Asserts our page structs against REAL bytes captured from a live ACC session — the
/// closeout of B1 ("parse a binary fixture page and assert known values"). The synthetic
/// layout tests pin offsets; these pin that the offsets decode real game data correctly.
///
/// Dump context (see <c>Fixtures/README.md</c>): BMW M4 GT3, Spa, practice, car stationary
/// on track with the engine running (status LIVE — NOT paused, which would zero the physics
/// page). Floats are asserted with tolerance; the packetId counter only as &gt; 0.
/// </summary>
public sealed class AccRealDumpTests
{
    private static byte[] LoadDump(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        File.Exists(path).Should().BeTrue(
            $"fixture '{name}' must be copied next to the test assembly (see csproj <None Include=\"Fixtures/*.bin\">)");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Static_dump_decodes_known_session_metadata()
    {
        AccStaticPage page = AccPageMarshaller.Read<AccStaticPage>(LoadDump("acc_static.bin"));

        page.SmVersion.Should().Be("1.9");
        page.AcVersion.Should().Be("1.7");
        page.CarModel.Should().Be("bmw_m4_gt3");
        page.Track.Should().Be("Spa");
        page.NumCars.Should().Be(1);
    }

    [Fact]
    public void Graphics_dump_decodes_live_practice_session()
    {
        AccGraphicsPage page = AccPageMarshaller.Read<AccGraphicsPage>(LoadDump("acc_graphics.bin"));

        page.PacketId.Should().BeGreaterThan(0);
        page.Status.Should().Be(2, "the dump was captured in AC_LIVE, not paused");
        page.Session.Should().Be(0, "session type is PRACTICE");
        page.IsInPit.Should().Be(0, "car was out on track");
    }

    [Fact]
    public void Physics_dump_decodes_live_idle_values()
    {
        byte[] bytes = LoadDump("acc_physics.bin");

        AccPageMarshaller.ReadPacketId(bytes).Should().BeGreaterThan(0);

        AccPhysicsPage page = AccPageMarshaller.Read<AccPhysicsPage>(bytes);

        // Stationary, engine running: native gear 2 = first gear (0=R, 1=N, 2=first).
        page.Gear.Should().Be(2);
        page.Rpm.Should().Be(1661);
        page.SpeedKmh.Should().BeApproximately(0f, 0.5f);
        page.Fuel.Should().BeApproximately(21.22f, 0.5f);
        page.Gas.Should().BeApproximately(0f, 0.01f);
        page.Brake.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void Physics_dump_has_warm_tyres_in_FL_FR_RL_RR_order()
    {
        AccPhysicsPage page = AccPageMarshaller.Read<AccPhysicsPage>(LoadDump("acc_physics.bin"));

        // [FL, FR, RL, RR]; warmed GT3 slicks, ~27 psi / ~78 °C core.
        page.WheelsPressure.Should().HaveCount(4);
        page.WheelsPressure[0].Should().BeApproximately(27.33f, 0.5f);
        page.WheelsPressure[1].Should().BeApproximately(27.65f, 0.5f);
        page.WheelsPressure[2].Should().BeApproximately(27.06f, 0.5f);
        page.WheelsPressure[3].Should().BeApproximately(27.62f, 0.5f);

        page.TyreCoreTemperature.Should().HaveCount(4);
        page.TyreCoreTemperature[0].Should().BeApproximately(77.64f, 0.5f);
        page.TyreCoreTemperature[1].Should().BeApproximately(79.01f, 0.5f);
        page.TyreCoreTemperature[2].Should().BeApproximately(76.86f, 0.5f);
        page.TyreCoreTemperature[3].Should().BeApproximately(78.93f, 0.5f);
    }
}
