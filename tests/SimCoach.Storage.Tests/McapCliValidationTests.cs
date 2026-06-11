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

    private static async Task<(int ExitCode, string Output)?> RunMcapDoctorAsync(string filePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "mcap",
                ArgumentList = { "doctor", filePath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
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
                return (-1, "mcap doctor timed out after 10 s");
            }

            return (process.ExitCode, await stdout + await stderr);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // mcap binary not on PATH
        }
    }
}
