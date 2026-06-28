using System.Globalization;
using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class PhraseRendererTests
{
    private static CoachAction Action(string template, params ParamBinding[] paramz) =>
        new(
            "test_action",
            "test_label",
            CoachCadence.Corner,
            new CoachPriority(CoachPhase.Brake, 1),
            RequiresReference: false,
            When: [],
            Params: paramz,
            template,
            HintEn: "test_hint");

    private static DictionaryGoldView Gold(
        IReadOnlyDictionary<string, double>? numbers = null,
        IReadOnlyDictionary<string, string>? strings = null) =>
        new(CoachCadence.Corner, hasReference: true, numbers: numbers, strings: strings);

    [Fact]
    public void Substitutes_corner_name_and_rounded_meters()
    {
        CoachAction action = Action(
            "В {corner} тормози позже на {meters}.",
            new ParamBinding("corner", "corner_name", ParamTransform.None, null),
            new ParamBinding("meters", "brake_point_diff_m", ParamTransform.AbsRound0, "м"));

        RenderedAction rendered = PhraseRenderer.Render(
            action,
            Gold(
                numbers: new Dictionary<string, double> { ["brake_point_diff_m"] = -3.4 },
                strings: new Dictionary<string, string> { ["corner_name"] = "Eau Rouge" }));

        rendered.PhraseRu.Should().Be("В Eau Rouge тормози позже на 3м.");
        rendered.RenderedParam.Should().Be("3м");
        rendered.ActionLabelShort.Should().Be("test_label");
    }

    [Theory]
    [InlineData(-3.4, "3")]
    [InlineData(3.6, "4")]
    public void AbsRound0_drops_sign_and_rounds(double value, string expected)
    {
        CoachAction action = Action(
            "{v}",
            new ParamBinding("v", "x", ParamTransform.AbsRound0, null));

        RenderedAction rendered = PhraseRenderer.Render(
            action,
            Gold(numbers: new Dictionary<string, double> { ["x"] = value }));

        rendered.PhraseRu.Should().Be(expected);
    }

    [Theory]
    [InlineData(-3.4, "-3м")]
    [InlineData(3.6, "+4м")]
    [InlineData(0.0, "0м")]
    public void SignedRound0_preserves_sign_with_unit(double value, string expected)
    {
        CoachAction action = Action(
            "{v}",
            new ParamBinding("v", "x", ParamTransform.SignedRound0, "м"));

        RenderedAction rendered = PhraseRenderer.Render(
            action,
            Gold(numbers: new Dictionary<string, double> { ["x"] = value }));

        rendered.PhraseRu.Should().Be(expected);
        rendered.RenderedParam.Should().Be(expected);
    }

    [Fact]
    public void Numeric_formatting_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            CoachAction action = Action(
                "{v}",
                new ParamBinding("v", "x", ParamTransform.None, null));

            RenderedAction rendered = PhraseRenderer.Render(
                action,
                Gold(numbers: new Dictionary<string, double> { ["x"] = 0.5 }));

            rendered.PhraseRu.Should().Be("0.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
