using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Builds the schema payload MCAP expects for protobuf-encoded channels: a serialized
/// <c>FileDescriptorSet</c> with the message's file and all transitive dependencies,
/// dependencies first, so sequential MCAP readers can decode the messages.
/// Note: our v1 files have no summary section, so index-dependent tooling is degraded —
/// `mcap doctor` passes, but `mcap cat` refuses unindexed files. Summary + zstd are the
/// documented follow-up alongside compression.
/// </summary>
public static class McapProtobufSchema
{
    public static byte[] BuildFileDescriptorSet(MessageDescriptor messageDescriptor)
    {
        ArgumentNullException.ThrowIfNull(messageDescriptor);

        FileDescriptorSet set = new();
        HashSet<string> visited = [];
        AddFileWithDependencies(messageDescriptor.File, set, visited);
        return set.ToByteArray();
    }

    private static void AddFileWithDependencies(FileDescriptor file, FileDescriptorSet set, HashSet<string> visited)
    {
        if (!visited.Add(file.Name))
        {
            return;
        }

        foreach (FileDescriptor dependency in file.Dependencies)
        {
            AddFileWithDependencies(dependency, set, visited);
        }

        set.File.Add(FileDescriptorProto.Parser.ParseFrom(file.SerializedData));
    }
}
