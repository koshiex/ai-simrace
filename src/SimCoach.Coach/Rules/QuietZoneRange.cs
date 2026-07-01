namespace SimCoach.Coach.Rules;

/// <summary>A normalized-lap-position range (0..1, inclusive) the user marked as a quiet zone.</summary>
public readonly record struct QuietZoneRange(double Start, double End)
{
    public bool Contains(double position) => position >= Start && position <= End;

    public void EnsureValid()
    {
        if (Start < 0 || End > 1 || Start > End)
        {
            throw new InvalidOperationException($"QuietZoneRange [{Start}, {End}] must satisfy 0 <= Start <= End <= 1.");
        }
    }
}
