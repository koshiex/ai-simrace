using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ClauseEvaluatorTests
{
    private static DictionaryGoldView Gold(double value) =>
        new(
            CoachCadence.Corner,
            hasReference: true,
            numbers: new Dictionary<string, double> { ["x"] = value });

    [Theory]
    [InlineData(ClauseOp.Lt, -3.0, -5.0, true)]
    [InlineData(ClauseOp.Lt, -3.0, -1.0, false)]
    [InlineData(ClauseOp.Lte, -3.0, -3.0, true)]
    [InlineData(ClauseOp.Gt, 2.0, 5.0, true)]
    [InlineData(ClauseOp.Gt, 2.0, 2.0, false)]
    [InlineData(ClauseOp.Gte, 2.0, 2.0, true)]
    [InlineData(ClauseOp.AbsGt, 3.0, -5.0, true)]
    [InlineData(ClauseOp.AbsGt, 3.0, -1.0, false)]
    [InlineData(ClauseOp.AbsLt, 3.0, -1.0, true)]
    [InlineData(ClauseOp.AbsLt, 3.0, -5.0, false)]
    public void Evaluates_numeric_ops(ClauseOp op, double threshold, double actual, bool expected)
    {
        var clause = new WhenClause("x", op, threshold, Bool: null);

        ClauseEvaluator.Evaluate(clause, Gold(actual)).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Evaluates_eq_on_a_bool_field(bool wanted, bool actual, bool expected)
    {
        var clause = new WhenClause("off_track", ClauseOp.Eq, Number: null, Bool: wanted);
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: true,
            bools: new Dictionary<string, bool> { ["off_track"] = actual });

        ClauseEvaluator.Evaluate(clause, gold).Should().Be(expected);
    }

    [Fact]
    public void Neq_is_the_inverse_of_eq_when_the_field_is_present()
    {
        var clause = new WhenClause("off_track", ClauseOp.Neq, Number: null, Bool: false);
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: true,
            bools: new Dictionary<string, bool> { ["off_track"] = true });

        ClauseEvaluator.Evaluate(clause, gold).Should().BeTrue();
    }

    [Theory]
    [InlineData(ClauseOp.Lt)]
    [InlineData(ClauseOp.Gt)]
    [InlineData(ClauseOp.AbsGt)]
    [InlineData(ClauseOp.Eq)]
    [InlineData(ClauseOp.Neq)]
    public void Returns_false_when_the_field_is_absent(ClauseOp op)
    {
        var clause = new WhenClause("missing", op, 0.0, Bool: op is ClauseOp.Eq or ClauseOp.Neq ? false : null);
        var gold = new DictionaryGoldView(CoachCadence.Corner, hasReference: true);

        ClauseEvaluator.Evaluate(clause, gold).Should().BeFalse();
    }
}
