namespace SimCoach.GhostImport;

/// <summary>
/// Resolves the SimCoach data root the same way the App does (<c>TelemetryComposition.ResolveDataRoot</c>):
/// an explicit value (the <c>--data-root</c> arg or <c>Storage:DataRoot</c>) with <c>%VAR%</c> expansion,
/// else the platform default <c>%LOCALAPPDATA%/SimCoach</c>. The importer writes the <c>alien_line</c> row
/// and its parquet under this root so the App reads exactly where the tool wrote (the derived vendored copy
/// is embedded separately by <c>AlienLineDataset</c>; this path is the dev-time local write).
/// </summary>
internal static class DataRootResolver
{
    /// <summary>Resolves the data root from an optional configured value, falling back to the platform default.</summary>
    internal static string Resolve(string? configuredDataRoot)
    {
        string expanded = Environment.ExpandEnvironmentVariables(configuredDataRoot ?? string.Empty);
        bool useDefault = string.IsNullOrWhiteSpace(expanded) || expanded.Contains('%');
        return useDefault
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimCoach")
            : expanded;
    }

    /// <summary>The <c>references</c> subtree under the data root, where reference parquets live.</summary>
    internal static string ReferencesDirectory(string dataRoot) => Path.Combine(dataRoot, "references");

    /// <summary>The SQLite database path under the data root (mirrors the App's default layout).</summary>
    internal static string DatabasePath(string dataRoot) => Path.Combine(dataRoot, "simcoach.db");
}
