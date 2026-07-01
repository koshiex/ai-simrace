namespace SimCoach.LLM;

/// <summary>
/// Token accounting for one call. <see cref="InputTokens"/> is the TOTAL prompt token count and is
/// INCLUSIVE of <see cref="CachedInputTokens"/> (cache hits are a subset of input, never added on top).
/// PR-F cost math depends on this convention:
/// <c>cost = (Input−Cached)/1e6·InRate + Cached/1e6·CachedRate + (Output+Reasoning)/1e6·OutRate</c>;
/// reasoning tokens bill at the output rate. OpenAI/OpenRouter <c>prompt_tokens</c> is already inclusive;
/// Anthropic <c>input_tokens</c> EXCLUDES cache, so its mapper folds <c>cache_read_input_tokens</c> into
/// <see cref="InputTokens"/>.
/// </summary>
public sealed record LlmUsage(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens = 0,
    int ReasoningTokens = 0);
