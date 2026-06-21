namespace SimCoach.Storage.Mcap;

/// <summary>Chunk compression codecs supported by <see cref="McapWriter"/>.</summary>
public enum McapCompression
{
    /// <summary>No compression — chunk records stored verbatim (<c>compression: ""</c>).</summary>
    None,

    /// <summary>Zstandard compression (<c>compression: "zstd"</c>).</summary>
    Zstd,
}
