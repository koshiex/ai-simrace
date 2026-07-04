namespace SimCoach.Coach.Actions;

/// <summary>
/// Pure evaluation of a <see cref="WhenClause"/> against an <see cref="IGoldView"/>. No reflection, no
/// <c>dynamic</c>, no expression strings — one switch over <see cref="ClauseOp"/>. A field the Gold view does
/// not carry evaluates to <c>false</c> for every operator (including <see cref="ClauseOp.Neq"/>).
/// </summary>
public static class ClauseEvaluator
{
    private enum EqualityResult
    {
        Absent,
        Equal,
        NotEqual,
    }

    public static bool Evaluate(WhenClause clause, IGoldView gold)
    {
        return clause.Op switch
        {
            ClauseOp.Eq => EvaluateEquality(clause, gold) == EqualityResult.Equal,
            ClauseOp.Neq => EvaluateEquality(clause, gold) == EqualityResult.NotEqual,
            _ => EvaluateNumeric(clause, gold),
        };
    }

    private static bool EvaluateNumeric(WhenClause clause, IGoldView gold)
    {
        if (clause.Number is not double target || !gold.TryGetNumber(clause.Field, out double value))
        {
            return false;
        }

        return clause.Op switch
        {
            ClauseOp.Lt => value < target,
            ClauseOp.Lte => value <= target,
            ClauseOp.Gt => value > target,
            ClauseOp.Gte => value >= target,
            ClauseOp.AbsGt => Math.Abs(value) > target,
            ClauseOp.AbsLt => Math.Abs(value) < target,
            _ => false,
        };
    }

    private static EqualityResult EvaluateEquality(WhenClause clause, IGoldView gold)
    {
        if (clause.Bool is bool wantedBool)
        {
            if (!gold.TryGetBool(clause.Field, out bool actual))
            {
                return EqualityResult.Absent;
            }

            return actual == wantedBool ? EqualityResult.Equal : EqualityResult.NotEqual;
        }

        if (clause.Number is double wantedNumber)
        {
            if (!gold.TryGetNumber(clause.Field, out double actual))
            {
                return EqualityResult.Absent;
            }

            return actual.Equals(wantedNumber) ? EqualityResult.Equal : EqualityResult.NotEqual;
        }

        if (clause.Text is string wantedText)
        {
            // An empty string is treated as absent (fail-closed), so an unset closed-set field like
            // CornerEvent.reason gates a `neq` clause out rather than spuriously satisfying it.
            if (!gold.TryGetString(clause.Field, out string actualText) || actualText.Length == 0)
            {
                return EqualityResult.Absent;
            }

            return string.Equals(actualText, wantedText, StringComparison.Ordinal)
                ? EqualityResult.Equal
                : EqualityResult.NotEqual;
        }

        return EqualityResult.Absent;
    }
}
