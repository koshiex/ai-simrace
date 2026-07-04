namespace SimCoach.Coach.Actions;

/// <summary>
/// One typed predicate over a single Gold field: <c>field op value</c> (no expression strings, no
/// field-vs-field). Exactly one of <see cref="Number"/> / <see cref="Bool"/> / <see cref="Text"/> is populated,
/// normalized from the JSON literal at load time; numeric ops read <see cref="Number"/>, equality ops
/// (<see cref="ClauseOp.Eq"/>/<see cref="ClauseOp.Neq"/>) read whichever of number/bool/string is set.
/// </summary>
public sealed record WhenClause(string Field, ClauseOp Op, double? Number, bool? Bool, string? Text = null);
