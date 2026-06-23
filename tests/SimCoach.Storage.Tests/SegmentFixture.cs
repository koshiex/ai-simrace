using Google.Protobuf;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;

namespace SimCoach.Storage.Tests;

/// <summary>
/// Writes telemetry frames into a session directory as one or more <c>segment-NNNN.mcap</c> files,
/// mirroring the recorder's layout, so the segment enumerator and Parquet writer can be tested
/// without a live capture. Frames are split across segments by a fixed count to force a boundary
/// inside a lap.
/// </summary>
internal static class SegmentFixture
{
    private const string Topic = "telemetry";
    private const string ProtobufEncoding = "protobuf";

    /// <summary>Writes <paramref name="frames"/> into <paramref name="sessionDirectory"/> split into segments of <paramref name="framesPerSegment"/>.</summary>
    public static void Write(string sessionDirectory, IReadOnlyList<TelemetryFrame> frames, int framesPerSegment)
    {
        Directory.CreateDirectory(sessionDirectory);
        byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);

        int segmentIndex = 0;
        for (int start = 0; start < frames.Count; start += framesPerSegment)
        {
            string path = Path.Combine(
                sessionDirectory,
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"segment-{segmentIndex:0000}.mcap"));
            using FileStream stream = File.Create(path);
            using var writer = new McapWriter(stream);
            ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, ProtobufEncoding, schemaData);
            ushort channelId = writer.AddChannel(schemaId, Topic, ProtobufEncoding);

            int end = Math.Min(start + framesPerSegment, frames.Count);
            for (int i = start; i < end; i++)
            {
                TelemetryFrame frame = frames[i];
                ulong logTimeNs = (ulong)frame.T.ToDateTimeOffset().ToUnixTimeMilliseconds() * 1_000_000UL;
                writer.WriteMessage(channelId, (uint)i, logTimeNs, logTimeNs, frame.ToByteArray());
            }

            segmentIndex++;
        }
    }
}
