using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimCoach.Coach.Gold;

/// <summary>
/// The single, deterministic Gold→string serializer — the ONLY place a Gold artifact becomes the text handed to
/// the LLM, so "only Gold-tier JSON leaves the machine" is mechanically enforceable here (asserted by the privacy
/// test). <see cref="JsonIgnoreCondition.WhenWritingNull"/> drops the nullable reference-relative / no-data
/// fields the builder left <c>null</c>; numbers are written culture-invariantly by <c>System.Text.Json</c>.
/// </summary>
public static class GoldSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<TEvent>(GoldArtifact<TEvent> artifact) =>
        JsonSerializer.Serialize(artifact, _options);
}
