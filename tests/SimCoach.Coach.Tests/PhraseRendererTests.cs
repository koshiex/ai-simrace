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

    private static readonly CoachOptions _options = new();

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
                strings: new Dictionary<string, string> { ["corner_name"] = "Eau Rouge" }),
            _options);

        rendered.PhraseRu.Should().Be("В Eau Rouge тормози позже на 3м.");
        rendered.RenderedParam.Should().Be("3м");
        rendered.ActionLabelShort.Should().Be("test_label");
    }

    [Theory]
    [InlineData(-8.5, "2 корпуса")]   // 8.5 / 4.6 → 1.85 → 2 (few)
    [InlineData(-4.5, "1 корпус")]    // 4.5 / 4.6 → 0.98 → 1 (one)
    [InlineData(-1.0, "1 корпус")]    // sub-half-car rounds up to a single length, never "0 корпусов"
    [InlineData(-23.0, "5 корпусов")] // 23 / 4.6 → 5 (many)
    public void CarLengths_converts_metres_to_the_pluralised_length(double meters, string expected)
    {
        CoachAction action = Action(
            "В {corner} тормози позже на {dist}.",
            new ParamBinding("corner", "corner_name", ParamTransform.None, null),
            new ParamBinding("dist", "brake_point_diff_m", ParamTransform.CarLengths, null));

        RenderedAction rendered = PhraseRenderer.Render(
            action,
            Gold(
                numbers: new Dictionary<string, double> { ["brake_point_diff_m"] = meters },
                strings: new Dictionary<string, string> { ["corner_name"] = "Ла-Сурс" }),
            _options);

        rendered.PhraseRu.Should().Be($"В Ла-Сурс тормози позже на {expected}.");
        rendered.RenderedParam.Should().Be(expected);
    }

    [Fact]
    public void Lap_pb_renders_without_a_dangling_placeholder()
    {
        CoachAction lapPb = ActionRegistry.Load().Actions.Single(a => a.Id == "lap_pb");

        RenderedAction rendered = PhraseRenderer.Render(
            lapPb, new DictionaryGoldView(CoachCadence.Lap, hasReference: false), _options);

        rendered.PhraseRu.Should().Be("Личный рекорд! Так держать.");
        rendered.PhraseRu.Should().NotContain("{");
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
            Gold(numbers: new Dictionary<string, double> { ["x"] = value }),
            _options);

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
            Gold(numbers: new Dictionary<string, double> { ["x"] = value }),
            _options);

        rendered.PhraseRu.Should().Be(expected);
        rendered.RenderedParam.Should().Be(expected);
    }

    [Fact]
    public void ReasonRu_glosses_the_code_and_is_not_promoted_to_the_chip()
    {
        // M21: the reason gloss is a string, not a quantitative token, so it renders into the phrase but
        // never becomes the overlay RenderedParam chip (which stays number-or-nothing).
        CoachAction action = Action(
            "В {corner} теряешь: {reason}.",
            new ParamBinding("corner", "corner_name", ParamTransform.None, null),
            new ParamBinding("reason", "reason", ParamTransform.ReasonRu, null));

        RenderedAction rendered = PhraseRenderer.Render(
            action,
            Gold(strings: new Dictionary<string, string>
            {
                ["corner_name"] = "Eau Rouge",
                ["reason"] = "early_brake",
            }),
            _options);

        rendered.PhraseRu.Should().Be("В Eau Rouge теряешь: раннее торможение.");
        rendered.RenderedParam.Should().BeEmpty();
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
                Gold(numbers: new Dictionary<string, double> { ["x"] = 0.5 }),
            _options);

            rendered.PhraseRu.Should().Be("0.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
