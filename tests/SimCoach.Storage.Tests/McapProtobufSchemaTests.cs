using FluentAssertions;
using Google.Protobuf.Reflection;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class McapProtobufSchemaTests
{
    [Fact]
    public void Descriptor_set_contains_the_message_file_and_its_dependencies()
    {
        // Act
        byte[] descriptorSetBytes = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);

        // Assert — MCAP protobuf schemas must ship the full transitive FileDescriptorSet
        FileDescriptorSet parsed = FileDescriptorSet.Parser.ParseFrom(descriptorSetBytes);
        List<string> fileNames = [.. parsed.File.Select(file => file.Name)];
        fileNames.Should().Contain("Schemas/telemetry.proto"); // Grpc.Tools keeps the folder prefix
        fileNames.Should().Contain("google/protobuf/timestamp.proto");
    }

    [Fact]
    public void Dependencies_precede_dependents_in_the_set()
    {
        // Act
        byte[] descriptorSetBytes = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);

        // Assert
        FileDescriptorSet parsed = FileDescriptorSet.Parser.ParseFrom(descriptorSetBytes);
        List<string> fileNames = [.. parsed.File.Select(file => file.Name)];
        fileNames.IndexOf("google/protobuf/timestamp.proto")
            .Should().BeLessThan(fileNames.IndexOf("Schemas/telemetry.proto"));
    }
}
