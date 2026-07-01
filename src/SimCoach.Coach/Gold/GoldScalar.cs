namespace SimCoach.Coach.Gold;

/// <summary>
/// Shared scalar adapters for the typed <c>IGoldView</c> wrappers: project a Gold record property to the
/// view's <c>TryGet*</c> contract. A nullable property that is <c>null</c> returns <c>false</c> — mirroring the
/// JSON field-drop and keeping the clause evaluator fail-closed without a reference.
/// </summary>
internal static class GoldScalar
{
    public static bool Num(double v, out double value)
    {
        value = v;
        return true;
    }

    public static bool Num(double? v, out double value)
    {
        value = v ?? 0d;
        return v.HasValue;
    }

    public static bool Num(int v, out double value)
    {
        value = v;
        return true;
    }

    public static bool Num(int? v, out double value)
    {
        value = v ?? 0d;
        return v.HasValue;
    }

    public static bool Str(string? v, out string value)
    {
        value = v ?? string.Empty;
        return v is not null;
    }
}
