using FluentAssertions;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Data-root resolution (PR-B3 commit 21): the importer resolves the same root the App reads
/// (<c>TelemetryComposition.ResolveDataRoot</c>) so it writes where the App looks — an explicit value with
/// <c>%VAR%</c> expansion, else the platform default.
/// </summary>
public sealed class DataRootResolverTests
{
    [Fact]
    public void Resolve_uses_an_explicit_root_when_provided()
    {
        string root = Path.Combine(Path.GetTempPath(), "simcoach-explicit-root");

        DataRootResolver.Resolve(root).Should().Be(root);
    }

    [Fact]
    public void Resolve_falls_back_to_local_appdata_when_blank_or_unexpandable()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimCoach");

        DataRootResolver.Resolve(null).Should().Be(expected);
        DataRootResolver.Resolve("   ").Should().Be(expected);
        // An unexpandable %VAR% token falls back rather than writing a literal-percent path.
        DataRootResolver.Resolve("%SIMCOACH_UNSET_TOKEN%").Should().Be(expected);
    }

    [Fact]
    public void References_and_database_paths_hang_off_the_resolved_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "simcoach-root");

        DataRootResolver.ReferencesDirectory(root).Should().Be(Path.Combine(root, "references"));
        DataRootResolver.DatabasePath(root).Should().Be(Path.Combine(root, "simcoach.db"));
    }
}
