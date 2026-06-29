using System.Globalization;
using SimCoach.Storage.Configuration;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// <see cref="ISettingsStore"/> over <see cref="SettingsRepository"/> — the write side surfaced by the P5
/// settings UI. After any write it signals the configuration source to re-read: model/<c>Llm:Live</c>/reasoning
/// changes re-bind the running options live via <c>IOptionsMonitor&lt;LlmOptions&gt;</c>; the monthly budget is a
/// config row that binds at startup (its live re-bind lands with the P5 UI that drives this store).
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private static readonly string[] _cadenceKeys = ["corner", "sector", "lap", "debrief"];

    private readonly SettingsRepository _repository;
    private readonly ISettingsReloadSignal _reload;
    private readonly TimeProvider _timeProvider;

    public SqliteSettingsStore(SettingsRepository repository, ISettingsReloadSignal reload, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(reload);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _reload = reload;
        _timeProvider = timeProvider;
    }

    public Task<string?> GetModelIdAsync(string cadenceKey, CancellationToken ct) =>
        _repository.GetAsync($"model.{ValidateCadence(cadenceKey)}", ct);

    public Task SetModelIdAsync(string cadenceKey, string modelId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return WriteAsync($"model.{ValidateCadence(cadenceKey)}", modelId, ct);
    }

    public async Task<decimal?> GetMonthlyBudgetUsdAsync(CancellationToken ct)
    {
        string? value = await _repository.GetAsync("budget.monthly_usd", ct).ConfigureAwait(false);
        return value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public Task SetMonthlyBudgetUsdAsync(decimal usd, CancellationToken ct) =>
        WriteAsync("budget.monthly_usd", usd.ToString(CultureInfo.InvariantCulture), ct);

    public async Task<bool?> GetLlmLiveAsync(CancellationToken ct)
    {
        string? value = await _repository.GetAsync("llm.live", ct).ConfigureAwait(false);
        return value is null ? null : bool.Parse(value);
    }

    public Task SetLlmLiveAsync(bool live, CancellationToken ct) =>
        WriteAsync("llm.live", live ? "true" : "false", ct);

    public Task<string?> GetAsync(string key, CancellationToken ct) => _repository.GetAsync(key, ct);

    public Task SetAsync(string key, string value, CancellationToken ct) => WriteAsync(key, value, ct);

    private async Task WriteAsync(string key, string value, CancellationToken ct)
    {
        await _repository.SetAsync(key, value, _timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
        _reload.Reload();
    }

    private static string ValidateCadence(string cadenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenceKey);
        if (!_cadenceKeys.Contains(cadenceKey))
        {
            throw new ArgumentException(
                $"Unknown cadence key '{cadenceKey}'; expected one of {string.Join(", ", _cadenceKeys)}.",
                nameof(cadenceKey));
        }

        return cadenceKey;
    }
}
