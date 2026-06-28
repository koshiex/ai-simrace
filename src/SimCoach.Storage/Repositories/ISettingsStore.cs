namespace SimCoach.Storage.Repositories;

/// <summary>
/// Typed async settings accessor over the <c>settings</c> key/value table (UI doc §3.8). Writes to a
/// configuration-bound key (model / monthly budget / live flag) re-bind the running options via the
/// SQLite configuration source; the generic <see cref="GetAsync"/>/<see cref="SetAsync"/> cover the
/// remaining UI-only keys (theme, locale, hotkeys, …).
/// </summary>
public interface ISettingsStore
{
    /// <param name="cadenceKey">One of <c>corner</c>/<c>sector</c>/<c>lap</c>/<c>debrief</c>.</param>
    Task<string?> GetModelIdAsync(string cadenceKey, CancellationToken ct);

    Task SetModelIdAsync(string cadenceKey, string modelId, CancellationToken ct);

    Task<decimal?> GetMonthlyBudgetUsdAsync(CancellationToken ct);

    Task SetMonthlyBudgetUsdAsync(decimal usd, CancellationToken ct);

    Task<bool?> GetLlmLiveAsync(CancellationToken ct);

    Task SetLlmLiveAsync(bool live, CancellationToken ct);

    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string value, CancellationToken ct);
}
