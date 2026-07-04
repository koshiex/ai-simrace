namespace SimCoach.Coach.Rules;

/// <summary>
/// The cross-lap dedup key for a candidate real-time tip (M32): which corner it addresses and the action
/// under consideration. Passed <c>in</c> to <see cref="RuleEngine.ShouldSpeak"/> — a struct rather than a
/// bare <c>string? cornerId</c> so the signature future-proofs against additional identity fields. A blank
/// <see cref="CornerId"/> (sector/lap summaries carry none) makes the dedup gate fail OPEN, the same
/// discipline as the frame gates. On the pre-LLM gate the <see cref="ActionId"/> is the lead action
/// (<c>subset[0].Id</c>); the post-emit <see cref="RuleEngine.NoteTip"/> instead records the ACTUAL spoken
/// action, so the gate reads the lead while memory reflects what the driver heard.
/// </summary>
public readonly record struct TipIdentity(string? CornerId, string? ActionId);
