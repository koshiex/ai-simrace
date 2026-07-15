using System.Globalization;
using System.Runtime.Versioning;
using SimCoach.Adapters.ACC.SharedMemory;

// Diagnostic probe for the replay-capture question (option B): does ACC keep the shared-memory pages
// live during a REPLAY, and which car do the channels follow? Reads the raw pages directly, BYPASSING
// the production new-frame gate (AccFrameAcquisition only emits when physics packetId advances), so it
// reveals a frozen packetId instead of silently recording nothing. Windows-only (reads Local\acpmf_*).
//   usage: SimCoach.ShmProbe            (run while ACC plays a replay focused on a fast car)
[SupportedOSPlatform("windows")]
internal static class Program
{
    private const int Iterations = 200;
    private const int SampleDelayMs = 100;
    private const int PrintEvery = 10;
    private const int FocusCountdownSec = 6;
    private const int ConnectDelayMs = 100;
    private const int MaxConnectTries = 40;
    private const int StatusReplay = 1;
    private const int StatusLive = 2;

    private static int Main()
    {
        using var source = new MemoryMappedAccPageSource();

        int connectTries = 0;
        while (!source.TryConnect())
        {
            if (++connectTries > MaxConnectTries)
            {
                Console.Error.WriteLine("could not connect to ACC shared memory — is ACC running?");
                return 2;
            }

            Thread.Sleep(ConnectDelayMs);
        }

        Console.WriteLine("connected to ACC shared memory.");
        for (int s = FocusCountdownSec; s > 0; s--)
        {
            Console.WriteLine($"  → click into ACC and PLAY the replay (focused). Sampling starts in {s}…");
            Thread.Sleep(1000);
        }

        Console.WriteLine("sampling (raw, bypassing the new-frame gate)…");
        Console.WriteLine(
            "iter | physPid gfxPid | st act playerId slot | speed  gas brk gear |  gLat gLong |   worldX   worldZ | track/car");

        byte[] physBuf = new byte[AccPhysicsPage.SizeBytes];
        byte[] gfxBuf = new byte[AccGraphicsPage.SizeBytes];
        byte[] statBuf = new byte[AccStaticPage.SizeBytes];

        int firstPhysPid = 0;
        int lastPhysPid = 0;
        int physPidChanges = 0;
        int replayCount = 0;
        int liveCount = 0;
        float firstX = float.NaN;
        float firstZ = float.NaN;
        float maxCoordShift = 0f;

        for (int i = 0; i < Iterations; i++)
        {
            source.TryReadPacketId(AccPage.Physics, out int physPid);
            source.TryReadPacketId(AccPage.Graphics, out int gfxPid);
            source.TryCopyPage(AccPage.Physics, physBuf);
            source.TryCopyPage(AccPage.Graphics, gfxBuf);
            source.TryCopyPage(AccPage.Static, statBuf);

            AccPhysicsPage physics = AccPageMarshaller.Read<AccPhysicsPage>(physBuf);
            AccGraphicsPage graphics = AccPageMarshaller.Read<AccGraphicsPage>(gfxBuf);
            AccStaticPage info = AccPageMarshaller.Read<AccStaticPage>(statBuf);

            int slot = graphics.CarId is null ? -1 : Array.IndexOf(graphics.CarId, graphics.PlayerCarId);
            float worldX = float.NaN;
            float worldZ = float.NaN;
            if (slot >= 0 && graphics.CarCoordinates is { } coords && ((slot * 3) + 2) < coords.Length)
            {
                worldX = coords[slot * 3];
                worldZ = coords[(slot * 3) + 2];
            }

            float gLat = float.NaN;
            float gLong = float.NaN;
            if (physics.AccG is { Length: >= 3 } accG)
            {
                gLat = accG[0];
                gLong = accG[2];
            }

            if (i == 0)
            {
                firstPhysPid = physPid;
                firstX = worldX;
                firstZ = worldZ;
            }
            else if (physPid != lastPhysPid)
            {
                physPidChanges++;
            }

            lastPhysPid = physPid;
            if (graphics.Status == StatusReplay)
            {
                replayCount++;
            }
            else if (graphics.Status == StatusLive)
            {
                liveCount++;
            }

            if (!float.IsNaN(worldX) && !float.IsNaN(firstX))
            {
                float shift = MathF.Abs(worldX - firstX) + MathF.Abs(worldZ - firstZ);
                if (shift > maxCoordShift)
                {
                    maxCoordShift = shift;
                }
            }

            if (i % PrintEvery == 0)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,3} | {1,7} {2,6} | {3,2} {4,3} {5,4} {6,4} | {7,6:F1} {8:F2} {9:F2} {10,4} | {11,5:F2} {12,5:F2} | {13,8:F1} {14,8:F1} | {15}/{16}",
                    i, physPid, gfxPid, graphics.Status, graphics.ActiveCars, graphics.PlayerCarId, slot,
                    physics.SpeedKmh, physics.Gas, physics.Brake, physics.Gear, gLat, gLong, worldX, worldZ,
                    info.Track, info.CarModel));
            }

            Thread.Sleep(SampleDelayMs);
        }

        int otherCount = Iterations - replayCount - liveCount;
        Console.WriteLine();
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "SUMMARY: physicsPacketId changed {0}× over {1} samples (first={2}). status: REPLAY(1)={3} LIVE(2)={4} other={5}. player-slot worldPos max shift = {6:F1} m.",
            physPidChanges, Iterations, firstPhysPid, replayCount, liveCount, otherCount, maxCoordShift));
        Console.WriteLine(
            "HINT: physPidChanges==0 → ACC freezes physics in replay → SHM capture-from-replay is BLOCKED. "
            + "physPidChanges>0 with a large worldPos shift while REPLAY dominates → SHM ticks in replay → option B viable "
            + "(then confirm the channels follow the focused fast car, not the player's own).");
        return 0;
    }
}
