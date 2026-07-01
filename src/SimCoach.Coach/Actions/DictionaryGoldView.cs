namespace SimCoach.Coach.Actions;

/// <summary>
/// A dictionary-backed <see cref="IGoldView"/> for fixtures/tests and as the usable adapter until the typed
/// Gold records land. Fields not supplied are absent (the <c>TryGet*</c> return <c>false</c>).
/// </summary>
public sealed class DictionaryGoldView : IGoldView
{
    private readonly IReadOnlyDictionary<string, double> _numbers;
    private readonly IReadOnlyDictionary<string, bool> _bools;
    private readonly IReadOnlyDictionary<string, string> _strings;

    public DictionaryGoldView(
        CoachCadence cadence,
        bool hasReference,
        IReadOnlyDictionary<string, double>? numbers = null,
        IReadOnlyDictionary<string, bool>? bools = null,
        IReadOnlyDictionary<string, string>? strings = null)
    {
        Cadence = cadence;
        HasReference = hasReference;
        _numbers = numbers ?? new Dictionary<string, double>(StringComparer.Ordinal);
        _bools = bools ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        _strings = strings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public CoachCadence Cadence { get; }

    public bool HasReference { get; }

    public bool TryGetNumber(string field, out double value) => _numbers.TryGetValue(field, out value);

    public bool TryGetBool(string field, out bool value) => _bools.TryGetValue(field, out value);

    public bool TryGetString(string field, out string value)
    {
        if (_strings.TryGetValue(field, out string? found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
