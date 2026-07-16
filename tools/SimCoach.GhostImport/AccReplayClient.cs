using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace SimCoach.GhostImport;

/// <summary>
/// DEV-TIME accreplay.com fetch (fetch plan in <c>docs/05-implementation/b3-implementation-blueprint.md</c>).
/// Pulls the fastest public GT3 hotlap for a track and extracts the inner <c>.ghost</c> from the returned
/// ZIP. Never exercised by any test and never run in CI; the raw <c>.ghost</c> it returns is transient and
/// is never written to git (only the derived alien LINE is vendored). The driver name from the leaderboard
/// is dropped here and never stored (OD1); the owner is responsible for having rights to imported artifacts.
/// </summary>
internal static class AccReplayClient
{
    private const string ApiBase = "https://www.accreplay.com";

    // Browser headers turn accreplay's deliberate 403 on a bare request into a 200. Confirmed operational
    // detail (omitted from the format doc); it circumvents an access control — gated by owner sign-off (OD1).
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/124.0 Safari/537.36";

    // In-tool string -> accreplay numeric trackId map (no such map exists elsewhere in the repo). Only
    // monza=3 is confirmed; spa's id is unknown and must be discovered + re-validated before it is trusted.
    private static readonly Dictionary<string, int> _trackIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monza"] = 3,
    };

    internal static int TrackIdFor(string track)
    {
        if (!_trackIds.TryGetValue(track, out int id))
        {
            throw new ArgumentException($"no accreplay trackId known for track '{track}'", nameof(track));
        }

        return id;
    }

    /// <summary>
    /// GET the GT3 leaderboard and return the fastest entry (board is sorted fastest-first → position 1;
    /// OD2 ships the fastest GT3 lap per track regardless of car). Driver name is not read.
    /// </summary>
    internal static async Task<AccReplayLap> FetchFastestGt3LapAsync(
        HttpClient http, int trackId, CancellationToken cancellationToken)
    {
        var url = new Uri($"{ApiBase}/api/leaderboards/laps?trackId={trackId}&group=GT3");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddBrowserHeaders(request);

        using HttpResponseMessage response = await http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"accreplay leaderboard for trackId {trackId} returned no laps");
        }

        JsonElement fastest = root[0];
        long lapId = fastest.GetProperty("lapId").GetInt64();
        string car = fastest.TryGetProperty("car", out JsonElement carElement)
            ? carElement.GetString() ?? string.Empty
            : string.Empty;
        int lapTimeMs = ParseLapTimeMs(fastest.GetProperty("lapTime"));
        return new AccReplayLap(lapId, car, lapTimeMs);
    }

    /// <summary>
    /// GET <c>/api/laps/{lapId}/download-ghost</c> (a ZIP), extract the inner
    /// <c>GhostCars/Offline/&lt;track&gt;/Dry_*.ghost</c>, and return its raw bytes after verifying the
    /// container magic. The bytes are transient — decode-then-discard, never committed.
    /// </summary>
    internal static async Task<byte[]> DownloadGhostAsync(
        HttpClient http, long lapId, string track, CancellationToken cancellationToken)
    {
        var url = new Uri($"{ApiBase}/api/laps/{lapId}/download-ghost");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddBrowserHeaders(request);

        using HttpResponseMessage response = await http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        byte[] zipBytes = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return ExtractGhost(zipBytes, track);
    }

    private static byte[] ExtractGhost(byte[] zipBytes, string track)
    {
        using var zipStream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        string trackSegment = $"Offline/{track}/";
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".ghost", StringComparison.OrdinalIgnoreCase)
            && e.FullName.Contains(trackSegment, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException($"download-ghost ZIP held no .ghost under Offline/{track}/");
        }

        using Stream entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        byte[] ghost = buffer.ToArray();

        if (ghost.Length < sizeof(ulong)
            || System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(ghost) != GhostContainer.ChunkMagic)
        {
            throw new InvalidDataException($"extracted '{entry.FullName}' does not start with the ghost container magic");
        }

        return ghost;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.Referrer = new Uri($"{ApiBase}/");
    }

    private static int ParseLapTimeMs(JsonElement lapTime)
    {
        // The API may express the laptime as milliseconds (number) or an "MM:SS.mmm" string.
        if (lapTime.ValueKind == JsonValueKind.Number)
        {
            return lapTime.GetInt32();
        }

        string text = lapTime.GetString() ?? string.Empty;
        string[] parts = text.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return (int)Math.Round(((minutes * 60) + seconds) * 1000.0);
        }

        throw new InvalidDataException($"unrecognized accreplay lapTime '{text}'");
    }
}
