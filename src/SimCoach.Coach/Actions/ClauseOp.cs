namespace SimCoach.Coach.Actions;

/// <summary>The comparison a <see cref="WhenClause"/> applies between a Gold field and its literal value.</summary>
public enum ClauseOp
{
    Lt,
    Lte,
    Gt,
    Gte,
    Eq,
    Neq,
    AbsGt,
    AbsLt,
}
