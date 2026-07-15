namespace SimCoach.Reference;

/// <summary>
/// The kinds of reference sharing the single <c>[references]</c> table (ADR-0021). Each maps to a
/// stable DB string used as the <c>kind</c> discriminator. <c>SimCoach.Storage</c> must not depend on
/// this assembly, so its <c>ReferenceRow.Kind</c> is the raw DB string; the enum and its mapping live
/// here. <c>alien_line</c> is deferred to PR-B3 and intentionally absent.
/// </summary>
public enum ReferenceKind
{
    /// <summary>Full-Parquet personal best: TIME via boundary <c>TimeAt</c>, LINE via world coords.</summary>
    Pb,

    /// <summary>Row-only own-optimal: N per-sector best durations as JSON, no Parquet (ADR-0021).</summary>
    Optimal,
}

/// <summary>Maps <see cref="ReferenceKind"/> to and from its stable <c>[references].kind</c> DB string.</summary>
public static class ReferenceKinds
{
    private const string PbString = "pb";
    private const string OptimalString = "optimal";

    /// <summary>The DB <c>kind</c> string for <paramref name="kind"/>.</summary>
    public static string ToDbString(this ReferenceKind kind) => kind switch
    {
        ReferenceKind.Pb => PbString,
        ReferenceKind.Optimal => OptimalString,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown reference kind."),
    };

    /// <summary>Parses a DB <c>kind</c> string; throws on any value outside the taxonomy.</summary>
    public static ReferenceKind Parse(string dbString)
    {
        ArgumentNullException.ThrowIfNull(dbString);
        return dbString switch
        {
            PbString => ReferenceKind.Pb,
            OptimalString => ReferenceKind.Optimal,
            _ => throw new ArgumentException($"Unknown reference kind '{dbString}'.", nameof(dbString)),
        };
    }
}
