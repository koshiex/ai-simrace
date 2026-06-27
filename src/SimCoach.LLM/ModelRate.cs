namespace SimCoach.LLM;

/// <summary>Per-million-token USD rates for one model. Zero is allowed (free/local models).</summary>
public sealed record ModelRate
{
    public decimal InputPerMillion { get; init; }

    public decimal OutputPerMillion { get; init; }

    public decimal CachedInputPerMillion { get; init; }

    public void EnsureValid()
    {
        if (InputPerMillion < 0m || OutputPerMillion < 0m || CachedInputPerMillion < 0m)
        {
            throw new InvalidOperationException("ModelRate values must be non-negative.");
        }
    }
}
