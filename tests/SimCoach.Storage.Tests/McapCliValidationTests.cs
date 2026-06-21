using System.Diagnostics;
using FluentAssertions;
using Google.Protobuf;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

/// <summary>
/// Validates our hand-rolled writer against the official `mcap` CLI when it is installed
/// (brew install mcap). Passes trivially where the CLI is absent (CI) — the byte-level
/// format tests still hold; this is an extra interoperability check for dev machines.
/// </summary>
public sealed class McapCliValidationTests
{
    [Fact]
    public async Task Mcap_cli_doctor_accepts_our_file_when_cli_is_available()
    {
        // Arrange
        string filePath = Path.Combine(Path.GetTempPath(), $"simcoach-doctor-{Guid.NewGuid():N}.mcap");
        try
        {
            using (FileStream stream = File.Create(filePath))
            using (var writer = new McapWriter(stream))
            {
                byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);
                ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, "protobuf", schemaData);
                ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
                TelemetryFrame frame = new() { Sim = "acc", LapNumber = 1, SpeedMps = 42.5f };
                writer.WriteMessage(channelId, 0, 1_000_000_000UL, 1_000_000_000UL, frame.ToByteArray());
                writer.Finish();
            }

            // Act
            (int exitCode, string output)? result = await RunMcapDoctorAsync(filePath);
            if (result is null)
            {
                return; // CLI not installed — interoperability check skipped
            }

            // Assert
            result.Value.exitCode.Should().Be(0, $"mcap doctor must accept the file; output: {result.Value.output}");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Mcap_cli_cat_reads_a_zstd_file_with_summary_when_cli_is_available()
    {
        // Arrange — a zstd-compressed, summary-bearing file: the case `mcap cat` refused pre-B5
        string filePath = Path.Combine(Path.GetTempPath(), $"simcoach-cat-{Guid.NewGuid():N}.mcap");
        try
        {
            using (FileStream stream = File.Create(filePath))
            using (var writer = new McapWriter(stream, new McapWriterOptions { Compression = McapCompression.Zstd }))
            {
                byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);
                ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, "protobuf", schemaData);
                ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
                for (uint sequence = 0; sequence < 3; sequence++)
                {
                    TelemetryFrame frame = new() { Sim = "acc", LapNumber = 1, SpeedMps = sequence };
                    writer.WriteMessage(channelId, sequence, (sequence + 1) * 1_000_000_000UL, 0, frame.ToByteArray());
                }

                writer.Finish();
            }

            // Act
            (int exitCode, string output)? doctor = await RunMcapAsync("doctor", filePath);
            (int exitCode, string output)? cat = await RunMcapAsync("cat", filePath, "--json");
            if (doctor is null || cat is null)
            {
                return; // CLI not installed — interoperability check skipped
            }

            // Assert
            doctor.Value.exitCode.Should().Be(0, $"mcap doctor must accept the zstd file; output: {doctor.Value.output}");
            cat.Value.exitCode.Should().Be(0, $"mcap cat must read the summary-bearing file; output: {cat.Value.output}");
            cat.Value.output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Should().HaveCount(3, "cat emits one line per message");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static Task<(int ExitCode, string Output)?> RunMcapDoctorAsync(string filePath) =>
        RunMcapAsync("doctor", filePath);

    private static async Task<(int ExitCode, string Output)?> RunMcapAsync(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "mcap",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Drain both pipes concurrently — sequential ReadToEnd can deadlock on full buffers.
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return (-1, "mcap CLI timed out after 10 s");
            }

            return (process.ExitCode, await stdout + await stderr);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // mcap binary not on PATH
        }
    }
}
