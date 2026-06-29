using Microsoft.Extensions.Options;

namespace SimCoach.LLM.Tests;

/// <summary>A fixed <see cref="IOptionsMonitor{T}"/> for tests: <see cref="CurrentValue"/> never changes and
/// no change callbacks ever fire. Lets router tests construct an <see cref="LlmRouter"/> from a plain options
/// instance without standing up the DI options pipeline.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
