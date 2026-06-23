using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimCoach.Reference;

/// <summary>
/// Stores derived models as one JSON file per track under <c>&lt;rootDir&gt;/track_models/</c>,
/// mirroring the on-disk <c>references/*.parquet</c> layout (ADR-0011). File-per-track keeps the model
/// store off the hot sessions/laps SQLite and needs no schema migration.
/// </summary>
public sealed class JsonTrackModelRepository : ITrackModelRepository
{
    private const string SubDirectory = "track_models";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public JsonTrackModelRepository(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _directory = Path.Combine(rootDirectory, SubDirectory);
    }

    public TrackModel? Get(string trackId)
    {
        string path = PathFor(trackId);
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TrackModel>(stream, _jsonOptions);
    }

    public void Save(TrackModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Directory.CreateDirectory(_directory);
        using FileStream stream = File.Create(PathFor(model.TrackId));
        JsonSerializer.Serialize(stream, model, _jsonOptions);
    }

    private string PathFor(string trackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);

        // track_id is a normalized token (a-z, 0-9, underscore); reject anything that could escape
        // the directory rather than trusting the caller.
        foreach (char c in trackId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new ArgumentException($"Track id '{trackId}' contains unsupported characters.", nameof(trackId));
            }
        }

        return Path.Combine(_directory, $"{trackId}.json");
    }
}
