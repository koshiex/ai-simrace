namespace SimCoach.Storage.Database;

/// <summary>Location of the SimCoach SQLite database. Mirrors the <c>RecordingOptions</c> idiom.</summary>
public sealed record DatabaseOptions
{
    /// <summary>
    /// Full path to the SQLite file. Default resolves to %LOCALAPPDATA%/SimCoach/simcoach.db on
    /// Windows and the platform equivalent elsewhere.
    /// </summary>
    public string DbPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimCoach",
        "simcoach.db");

    /// <summary>Fails fast on unusable values. Called by consumers' constructors.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(DbPath))
        {
            throw new ArgumentException("A database path is required.", nameof(DbPath));
        }
    }
}
