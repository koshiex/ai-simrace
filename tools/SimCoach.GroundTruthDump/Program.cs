using System.Globalization;
using System.Text;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;

// Ground-truth dumper for the detection-truthfulness exit gate (Phase-3, TASK 7).
// Decodes a recorded session's rotating segment-*.mcap files through the SAME
// McapSegmentEnumerator the production pipeline uses, and writes one CSV row per raw frame.
// The Python truth-oracle (scripts/groundtruth_oracle.py) consumes this CSV independently of any
// pipeline math. Raw MCAP is never committed (privacy / .gitignore *.mcap); this tool + its output
// stay on the dev machine.
//   usage: SimCoach.GroundTruthDump <session-dir> <output-csv>
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: SimCoach.GroundTruthDump <session-dir> <output-csv>");
    return 2;
}

string sessionDir = args[0];
string outputPath = args[1];
if (!Directory.Exists(sessionDir) && !File.Exists(sessionDir))
{
    Console.Error.WriteLine($"session path not found: {sessionDir}");
    return 2;
}

const float MetresPerSecondToKmh = 3.6f;

static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

using var writer = new StreamWriter(outputPath, append: false, Encoding.ASCII);
writer.Write(
    "t_ms,normalized_car_position,speed_kmh,brake,throttle,gear,steer_angle,"
    + "is_in_pit_lane,is_valid_lap,tyres_out,current_sector_index,lap_number,"
    + "g_lat,g_long,world_x,world_z\n");

long count = 0;
long pitFrames = 0;
foreach (TelemetryFrame frame in McapSegmentEnumerator.Read(sessionDir))
{
    // Epoch-ms is the only reliable clock: ACC lap_number is garbage in this fixture (dump it for
    // debug only; the oracle segments by normalized_car_position wrap, never by lap_number).
    long tMs = frame.T is null ? 0L : frame.T.ToDateTimeOffset().ToUnixTimeMilliseconds();
    bool inPitLane = frame.IsInPitLane;
    if (inPitLane)
    {
        pitFrames++;
    }

    writer.Write(tMs.ToString(CultureInfo.InvariantCulture));
    writer.Write(',');
    writer.Write(F(frame.NormalizedCarPosition));
    writer.Write(',');
    writer.Write(F(frame.SpeedMps * MetresPerSecondToKmh));
    writer.Write(',');
    writer.Write(F(frame.BrakePct));
    writer.Write(',');
    writer.Write(F(frame.ThrottlePct));
    writer.Write(',');
    writer.Write(frame.Gear.ToString(CultureInfo.InvariantCulture));
    writer.Write(',');
    writer.Write(F(frame.SteerRad));
    writer.Write(',');
    writer.Write(inPitLane ? '1' : '0');
    writer.Write(',');
    writer.Write(frame.IsValidLap ? '1' : '0');
    writer.Write(',');
    writer.Write(frame.TyresOut.ToString(CultureInfo.InvariantCulture));
    writer.Write(',');
    writer.Write(frame.CurrentSectorIndex.ToString(CultureInfo.InvariantCulture));
    writer.Write(',');
    writer.Write(frame.LapNumber.ToString(CultureInfo.InvariantCulture));
    // g-force (GForceG.X = lateral, .Z = longitudinal) + world position (X, Z) — needed to calibrate the
    // grip envelope against MEASURED lateral g and to build a real racing line (not the median centerline).
    writer.Write(',');
    writer.Write(F(frame.GForceG?.X ?? 0f));
    writer.Write(',');
    writer.Write(F(frame.GForceG?.Z ?? 0f));
    writer.Write(',');
    writer.Write(F(frame.WorldPos?.X ?? 0f));
    writer.Write(',');
    writer.Write(F(frame.WorldPos?.Z ?? 0f));
    writer.Write('\n');
    count++;
}

Console.WriteLine($"WROTE {count} frames -> {outputPath}");
Console.WriteLine($"in_pit_lane frames: {pitFrames}");
return 0;
